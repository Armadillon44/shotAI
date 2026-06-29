//! shotAI element-at-point — Windows UI Automation.
//!
//! Exposes ONE C-ABI function, `element_at_point(x, y, out, cap)`, that returns
//! (as UTF-8 JSON written into the caller's buffer) the actionable, named UI
//! element under a screen point — e.g. the "OK" button the user clicked. It
//! climbs from the hit element up to the nearest *named, actionable* ancestor
//! (so clicking the text inside a button still resolves to the button), and
//! reports a control-type name + bounds. Works for native Win32/WPF controls
//! AND web content (Chromium exposes a UIA tree).
//!
//! Built as a plain cdylib (no napi-rs — that wants MSVC on Windows) and loaded
//! from the Electron main process via koffi. The whole body runs inside
//! catch_unwind so a panic can never unwind across the FFI boundary.
use std::cell::RefCell;
use std::ffi::c_int;
use std::panic::catch_unwind;

use windows::Win32::Foundation::{POINT, RECT};
use windows::Win32::System::Com::{
    CoCreateInstance, CoInitializeEx, CLSCTX_INPROC_SERVER, COINIT_MULTITHREADED,
};
use windows::Win32::UI::Accessibility::{CUIAutomation, IUIAutomation, IUIAutomationElement};

thread_local! {
    // One UIA instance per worker thread (koffi async calls run on a libuv pool
    // thread). COM is initialized MTA on first use of each thread.
    static UIA: RefCell<Option<IUIAutomation>> = const { RefCell::new(None) };
}

fn ensure_uia() -> Option<IUIAutomation> {
    UIA.with(|cell| {
        if let Some(u) = cell.borrow().as_ref() {
            return Some(u.clone());
        }
        unsafe {
            // MTA so UIA calls don't need a message pump and don't touch the
            // app's STA main thread. Already-initialized / changed-mode is fine.
            let _ = CoInitializeEx(None, COINIT_MULTITHREADED);
            let uia: IUIAutomation =
                CoCreateInstance(&CUIAutomation, None, CLSCTX_INPROC_SERVER).ok()?;
            *cell.borrow_mut() = Some(uia.clone());
            Some(uia)
        }
    })
}

/// Short programmatic name for a UIA control-type id (UIA_*ControlTypeId).
fn control_type_name(id: i32) -> &'static str {
    match id {
        50000 => "Button",
        50001 => "Calendar",
        50002 => "CheckBox",
        50003 => "ComboBox",
        50004 => "Edit",
        50005 => "Hyperlink",
        50006 => "Image",
        50007 => "ListItem",
        50008 => "List",
        50009 => "Menu",
        50010 => "MenuBar",
        50011 => "MenuItem",
        50012 => "ProgressBar",
        50013 => "RadioButton",
        50014 => "ScrollBar",
        50015 => "Slider",
        50016 => "Spinner",
        50017 => "StatusBar",
        50018 => "Tab",
        50019 => "TabItem",
        50020 => "Text",
        50021 => "ToolBar",
        50022 => "ToolTip",
        50023 => "Tree",
        50024 => "TreeItem",
        50025 => "Custom",
        50026 => "Group",
        50027 => "Thumb",
        50028 => "DataGrid",
        50029 => "DataItem",
        50030 => "Document",
        50031 => "SplitButton",
        50032 => "Window",
        50033 => "Pane",
        50034 => "Header",
        50035 => "HeaderItem",
        50036 => "Table",
        50037 => "TitleBar",
        50038 => "Separator",
        50039 => "SemanticZoom",
        50040 => "AppBar",
        _ => "Unknown",
    }
}

/// Control types whose Name is a stable LABEL (safe + useful for a caption),
/// vs. types whose Name is often free content (Text/Document/Pane) — excluded
/// so we never put a field's contents (or a block of page text) in a caption.
fn is_actionable(id: i32) -> bool {
    matches!(
        id,
        50000 // Button
            | 50002 // CheckBox
            | 50003 // ComboBox
            | 50004 // Edit (Name = the field label)
            | 50005 // Hyperlink
            | 50007 // ListItem
            | 50011 // MenuItem
            | 50013 // RadioButton
            | 50015 // Slider
            | 50016 // Spinner
            | 50018 // Tab
            | 50019 // TabItem
            | 50024 // TreeItem
            | 50029 // DataItem
            | 50031 // SplitButton
    )
}

unsafe fn name_of(el: &IUIAutomationElement) -> String {
    el.CurrentName().map(|b| b.to_string()).unwrap_or_default()
}
unsafe fn ctype_of(el: &IUIAutomationElement) -> i32 {
    el.CurrentControlType().map(|c| c.0).unwrap_or(0)
}

/// Resolve the element at a screen point to JSON. Returns the number of bytes
/// written to `out` (or, if `out` is null / `cap` <= 0, the number of bytes the
/// JSON needs). Negative = failure (no element / UIA unavailable / panic).
///
/// JSON shape: {name, controlType, controlTypeId, className, actionable, x,y,w,h}
/// `actionable` is true only when the resolved element is a label-bearing type
/// with a non-empty name — the caller uses it to decide whether to name the
/// element in the step caption.
#[no_mangle]
pub extern "C" fn element_at_point(x: c_int, y: c_int, out: *mut u8, cap: c_int) -> c_int {
    catch_unwind(|| unsafe {
        let uia = match ensure_uia() {
            Some(u) => u,
            None => return -1,
        };
        let hit = match uia.ElementFromPoint(POINT { x, y }) {
            Ok(e) => e,
            Err(_) => return -2,
        };

        // Climb self -> ancestors (capped) for the nearest named, actionable
        // control, so the text/icon inside a button resolves to the button.
        let walker = match uia.ControlViewWalker() {
            Ok(w) => w,
            Err(_) => return -3,
        };
        let mut chosen: Option<IUIAutomationElement> = None;
        let mut cur = Some(hit.clone());
        let mut depth = 0;
        while let Some(el) = cur {
            if depth >= 6 {
                break;
            }
            if !name_of(&el).is_empty() && is_actionable(ctype_of(&el)) {
                chosen = Some(el);
                break;
            }
            cur = walker.GetParentElement(&el).ok();
            depth += 1;
        }

        let actionable = chosen.is_some();
        let el = chosen.unwrap_or(hit);
        let name = name_of(&el);
        let ct = ctype_of(&el);
        let class = el
            .CurrentClassName()
            .map(|b| b.to_string())
            .unwrap_or_default();
        let r: RECT = el.CurrentBoundingRectangle().unwrap_or(RECT {
            left: 0,
            top: 0,
            right: 0,
            bottom: 0,
        });

        let json = serde_json::json!({
            "name": name,
            "controlType": control_type_name(ct),
            "controlTypeId": ct,
            "className": class,
            "actionable": actionable,
            "x": r.left,
            "y": r.top,
            "w": r.right - r.left,
            "h": r.bottom - r.top,
        })
        .to_string();

        let bytes = json.as_bytes();
        if out.is_null() || cap <= 0 {
            return bytes.len() as c_int;
        }
        let n = bytes.len().min(cap as usize);
        std::ptr::copy_nonoverlapping(bytes.as_ptr(), out, n);
        n as c_int
    })
    .unwrap_or(-99)
}
