using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace Toucan.Tests.Shared;

/// <summary>
/// Writes an assembly's public surface as stable text, one line per type and member, so a test can compare it with the frozen
/// baseline under <c>docs/api/</c>. Anything a plugin can see and call appears; private and internal details do not.
/// </summary>
internal static class PublicApi
{
    public static string Describe(Assembly assembly)
    {
        var sb = new StringBuilder();
        foreach (var type in assembly.GetExportedTypes().OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            sb.AppendLine(TypeLine(type));
            foreach (var member in Members(type).Order(StringComparer.Ordinal)) sb.Append("  ").AppendLine(member);
        }
        return sb.ToString().ReplaceLineEndings("\n");
    }

    private static string TypeLine(Type type)
    {
        var kind = type.IsEnum ? "enum" : type.IsInterface ? "interface" : type.IsValueType ? "struct"
            : typeof(Delegate).IsAssignableFrom(type) ? "delegate" : type.IsAbstract && type.IsSealed ? "static class"
            : IsRecord(type) ? (type.IsSealed ? "sealed record" : type.IsAbstract ? "abstract record" : "record")
            : type.IsSealed ? "sealed class" : type.IsAbstract ? "abstract class" : "class";
        var bases = new List<string>();
        if (type.BaseType is { } b && b != typeof(object) && b != typeof(ValueType) && b != typeof(Enum) && b != typeof(MulticastDelegate)) bases.Add(Name(b));
        bases.AddRange(type.GetInterfaces().Where(i => i.IsPublic || i.IsNestedPublic)
            .Where(i => !(type.BaseType?.GetInterfaces().Contains(i) ?? false))
            .Where(i => !i.IsGenericType || i.GetGenericTypeDefinition() != typeof(IEquatable<>)).Select(Name).Order(StringComparer.Ordinal));
        return $"{kind} {Name(type)}{(bases.Count > 0 ? " : " + string.Join(", ", bases) : "")}";
    }

    private static bool IsRecord(Type type) => type.GetMethod("<Clone>$") is not null;

    private static IEnumerable<string> Members(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        if (type.IsEnum)
        {
            foreach (var name in Enum.GetNames(type)) yield return $"{name} = {Convert.ToInt64(Enum.Parse(type, name), System.Globalization.CultureInfo.InvariantCulture)}";
            yield break;
        }
        var record = IsRecord(type);
        foreach (var m in type.GetMembers(flags))
        {
            if (m is MethodBase method && !(method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly)) continue;
            if (m is FieldInfo field && !(field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly)) continue;
            if (m.IsDefined(typeof(CompilerGeneratedAttribute)) && m is MethodInfo) continue;
            switch (m)
            {
                case ConstructorInfo c when !type.IsAbstract || c.IsFamily || c.IsPublic:
                    yield return $"{(c.IsStatic ? "static " : "")}ctor({Parameters(c)})";
                    break;
                case MethodInfo mi when !mi.IsSpecialName && !(record && mi.Name is "<Clone>$" or "PrintMembers" or "Deconstruct" or "Equals" or "GetHashCode" or "ToString" or "op_Equality" or "op_Inequality"):
                    yield return $"{Modifiers(mi)}{Name(mi.ReturnType)} {mi.Name}{Generic(mi)}({Parameters(mi)})";
                    break;
                case MethodInfo op when op.IsSpecialName && op.Name.StartsWith("op_", StringComparison.Ordinal) && !(record && op.Name is "op_Equality" or "op_Inequality"):
                    yield return $"static {Name(op.ReturnType)} {op.Name}({Parameters(op)})";
                    break;
                case PropertyInfo p when Visible(p.GetMethod) || Visible(p.SetMethod):
                    if (record && p.Name == "EqualityContract") break;
                    var accessors = (Visible(p.GetMethod) ? "get; " : "") + (Visible(p.SetMethod)
                        ? p.SetMethod!.ReturnParameter.GetRequiredCustomModifiers().Any(t => t.Name == "IsExternalInit") ? "init; " : "set; " : "");
                    var indexer = p.GetIndexParameters() is { Length: > 0 } ip ? $"[{string.Join(", ", ip.Select(x => Name(x.ParameterType)))}]" : "";
                    yield return $"{Modifiers(p.GetMethod ?? p.SetMethod!)}{Name(p.PropertyType)} {(indexer.Length > 0 ? "this" + indexer : p.Name)} {{ {accessors}}}";
                    break;
                case FieldInfo f:
                    yield return $"{(f.IsLiteral ? "const " : f.IsStatic ? "static " : "")}{(f.IsInitOnly ? "readonly " : "")}{Name(f.FieldType)} {f.Name}";
                    break;
                case EventInfo e when Visible(e.AddMethod):
                    yield return $"event {Name(e.EventHandlerType!)} {e.Name}";
                    break;
                case Type nested when nested.IsNestedPublic:
                    break; // Listed as its own type.
            }
        }
    }

    private static bool Visible(MethodInfo? m) => m is not null && (m.IsPublic || m.IsFamily || m.IsFamilyOrAssembly);

    private static string Modifiers(MethodInfo m) =>
        (m.IsStatic ? "static " : "") + (m.DeclaringType!.IsInterface ? m.IsAbstract ? "" : "default " : m.IsAbstract ? "abstract " : m.IsVirtual && !m.IsFinal ? m.GetBaseDefinition() != m ? "override " : "virtual " : "");

    private static string Generic(MethodInfo m) => m.IsGenericMethodDefinition ? $"<{string.Join(", ", m.GetGenericArguments().Select(a => a.Name))}>" : "";

    private static string Parameters(MethodBase m) => string.Join(", ", m.GetParameters().Select(p =>
        $"{(p.IsOut ? "out " : p.ParameterType.IsByRef ? "ref " : "")}{(p.IsDefined(typeof(ParamArrayAttribute)) ? "params " : "")}{Name(p.ParameterType)} {p.Name}{(p.HasDefaultValue ? " = " + (p.RawDefaultValue is null && p.ParameterType.IsValueType && Nullable.GetUnderlyingType(p.ParameterType) is null ? "default" : Literal(p.RawDefaultValue)) : "")}"));

    private static string Literal(object? value) => value switch
    {
        null => "null",
        string s => $"\"{s}\"",
        bool b => b ? "true" : "false",
        _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "null",
    };

    private static string Name(Type type)
    {
        if (type.IsByRef) return Name(type.GetElementType()!);
        if (type.IsArray) return Name(type.GetElementType()!) + "[]";
        if (type.IsGenericParameter) return type.Name;
        if (Nullable.GetUnderlyingType(type) is { } inner) return Name(inner) + "?";
        var name = type.IsNested ? Name(type.DeclaringType!) + "." + type.Name : (type.Namespace is { Length: > 0 } ns ? ns + "." : "") + type.Name;
        var tick = name.IndexOf('`', StringComparison.Ordinal);
        if (tick >= 0) name = name[..tick];
        return type.IsGenericType ? $"{name}<{string.Join(", ", type.GetGenericArguments().Select(Name))}>" : name;
    }
}
