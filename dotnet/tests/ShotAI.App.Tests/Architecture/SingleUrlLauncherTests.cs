using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ShotAI.Core.Store;
using ShotAI.Platform;
using Xunit;

namespace ShotAI.App.Tests.Architecture;

/// <summary>
/// INV-IPC-3 and ARCHITECTURE S15 (spec 11 8.2; AC-IPC-19 runs it, and stays WP-D12's): an IL
/// scan of the three product assemblies as built. Only <c>ShellUrlLauncher</c>,
/// <c>ShellReveal</c> and <c>ProcessStarter</c> (spec 12 7.10.4, which starts an exe with
/// <c>UseShellExecute = false</c> and never a URL) call any <c>Process.Start</c>, so the only
/// code that hands a URL to the shell is the launcher. RS0030 bans the call at build time
/// everywhere else; this reads what the compiler produced, lambdas and state machines included.
/// </summary>
public sealed class SingleUrlLauncherTests
{
    private static readonly string[] MayStartAProcess =
    [
        "ShotAI.Platform.Shell.ShellUrlLauncher",
        "ShotAI.Platform.Shell.ShellReveal",
        "ShotAI.Platform.Processes.ProcessStarter",
    ];

    // Every opcode by its encoded value, for the operand sizes.
    private static readonly Dictionary<ushort, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(f => (OpCode)f.GetValue(null)!)
        .ToDictionary(o => unchecked((ushort)o.Value));

    private static string[] ProductAssemblies =>
        [typeof(IProjectService).Assembly.Location, typeof(DllSearchHardening).Assembly.Location, typeof(App).Assembly.Location];

    /// <summary>
    /// The types whose code calls <c>System.Diagnostics.Process.Start</c>, any overload or the
    /// instance method, are among the three; the launcher and the reveal are found, which shows
    /// the scan reads the calls it is looking for.
    /// </summary>
    [Fact]
    public void OnlyTheLauncherTheRevealAndTheStarterStartAProcess()
    {
        var callers = ProductAssemblies.SelectMany(a => Callers(a, "System.Diagnostics", "Process", "Start")).Distinct().Order(StringComparer.Ordinal).ToList();
        TestContext.Current.TestOutputHelper?.WriteLine("Process.Start callers: " + string.Join(", ", callers));
        Assert.Contains("ShotAI.Platform.Shell.ShellUrlLauncher", callers);
        Assert.Contains("ShotAI.Platform.Shell.ShellReveal", callers);
        Assert.All(callers, c => Assert.Contains(c, MayStartAProcess));
    }

    /// <summary>No product assembly imports the shell's own <c>ShellExecute</c> functions, a way around <c>Process.Start</c>.</summary>
    [Fact]
    public void NothingImportsShellExecute()
    {
        foreach (var path in ProductAssemblies)
        {
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            var md = pe.GetMetadataReader();
            var imports = md.MethodDefinitions
                .Select(h => md.GetMethodDefinition(h).GetImport())
                .Where(i => !i.Module.IsNil)
                .Select(i => md.GetString(i.Name))
                .ToList();
            Assert.DoesNotContain(imports, n => n.StartsWith("ShellExecute", StringComparison.Ordinal));
        }
    }

    // The outermost declaring type of every method whose IL calls or loads Namespace.Type.Member.
    private static HashSet<string> Callers(string assemblyPath, string ns, string type, string member)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        var md = pe.GetMetadataReader();
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var typeHandle in md.TypeDefinitions)
        {
            foreach (var methodHandle in md.GetTypeDefinition(typeHandle).GetMethods())
            {
                var method = md.GetMethodDefinition(methodHandle);
                if (method.RelativeVirtualAddress == 0) continue;
                var il = pe.GetMethodBody(method.RelativeVirtualAddress).GetILReader();
                while (il.RemainingBytes > 0)
                {
                    var op = ReadOpCode(ref il);
                    if (op.OperandType == OperandType.InlineMethod)
                    {
                        // call, callvirt, newobj, ldftn, ldvirtftn and jmp.
                        if (Refers(md, MetadataTokens.EntityHandle(il.ReadInt32()), ns, type, member)) found.Add(Outermost(md, typeHandle));
                    }
                    else
                    {
                        SkipOperand(ref il, op);
                    }
                }
            }
        }
        return found;
    }

    private static OpCode ReadOpCode(ref BlobReader il)
    {
        ushort value = il.ReadByte();
        if (value == 0xFE) value = (ushort)(0xFE00 | il.ReadByte());
        return OpCodesByValue[value];
    }

    private static void SkipOperand(ref BlobReader il, OpCode op)
    {
        switch (op.OperandType)
        {
            case OperandType.InlineNone:
                break;
            case OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar:
                il.Offset += 1;
                break;
            case OperandType.InlineVar:
                il.Offset += 2;
                break;
            case OperandType.InlineI8 or OperandType.InlineR:
                il.Offset += 8;
                break;
            case OperandType.InlineSwitch:
                // The count first: il.Offset += 4 * il.ReadInt32() would read Offset before the count moves it.
                var targets = il.ReadInt32();
                il.Offset += 4 * targets;
                break;
            default:
                il.Offset += 4;
                break;
        }
    }

    private static bool Refers(MetadataReader md, EntityHandle handle, string ns, string type, string member)
    {
        switch (handle.Kind)
        {
            case HandleKind.MemberReference:
                var reference = md.GetMemberReference((MemberReferenceHandle)handle);
                return md.StringComparer.Equals(reference.Name, member) && IsType(md, reference.Parent, ns, type);
            case HandleKind.MethodDefinition:
                var definition = md.GetMethodDefinition((MethodDefinitionHandle)handle);
                return md.StringComparer.Equals(definition.Name, member) && IsType(md, definition.GetDeclaringType(), ns, type);
            case HandleKind.MethodSpecification:
                return Refers(md, md.GetMethodSpecification((MethodSpecificationHandle)handle).Method, ns, type, member);
            default:
                return false;
        }
    }

    // A generic instantiation (a TypeSpecification) is never the non-generic type looked for.
    private static bool IsType(MetadataReader md, EntityHandle parent, string ns, string type)
    {
        switch (parent.Kind)
        {
            case HandleKind.TypeReference:
                var reference = md.GetTypeReference((TypeReferenceHandle)parent);
                return md.StringComparer.Equals(reference.Name, type) && md.StringComparer.Equals(reference.Namespace, ns);
            case HandleKind.TypeDefinition:
                var definition = md.GetTypeDefinition((TypeDefinitionHandle)parent);
                return md.StringComparer.Equals(definition.Name, type) && md.StringComparer.Equals(definition.Namespace, ns);
            default:
                return false;
        }
    }

    // A lambda's closure, an iterator and an async state machine are nested types: they count as the type that declares them.
    private static string Outermost(MetadataReader md, TypeDefinitionHandle handle)
    {
        var definition = md.GetTypeDefinition(handle);
        while (definition.GetDeclaringType() is { IsNil: false } outer) definition = md.GetTypeDefinition(outer);
        var ns = md.GetString(definition.Namespace);
        var name = md.GetString(definition.Name);
        return ns.Length == 0 ? name : ns + "." + name;
    }
}
