using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace FcaBedrock.Orchestration.Tests;

/// <summary>
/// The test's own rendering of an assembly's public surface: one line per fact, sorted ordinally.
/// <para>
/// It lists every exported type (kind, full name, base type, interfaces, sealed, abstract or
/// static), every constructor of an exported type with its exact accessibility (private ones
/// included, because a closed family's claim is about who can construct it), every public or
/// protected member (static or instance, accessor visibility with <c>init</c> and
/// <c>required</c>, the member's type, parameter types, names and defaults, generic arity, and
/// reference-type nullability), and every enum member with its value. Compiler-generated record
/// members are listed too, because they are surface.
/// </para>
/// <para>
/// <b>Deliberately not <see cref="MemberInfo"/>'s own <c>ToString</c>,</b> whose format the
/// runtime does not document, so the pin cannot move with a runtime update (EP-23). It needs no
/// package.
/// </para>
/// </summary>
internal static class PublicSurfaceFormatter
{
    private const BindingFlags Declared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static
        | BindingFlags.DeclaredOnly;

    /// <summary>The surface of <paramref name="assembly"/>, one fact per line, in ordinal order.</summary>
    public static List<string> Lines(Assembly assembly)
    {
        var nullability = new NullabilityInfoContext();
        var lines = new List<string>();

        foreach (var type in assembly.GetExportedTypes())
        {
            var owner = TypeName(type);
            lines.Add(owner + ": " + TypeShape(type));

            if (type.IsEnum)
            {
                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
                {
                    var value = Convert.ToInt64(field.GetRawConstantValue(), CultureInfo.InvariantCulture);
                    lines.Add($"{owner}.{field.Name} = {value.ToString(CultureInfo.InvariantCulture)}");
                }

                continue;
            }

            foreach (var constructor in type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                lines.Add($"{owner}..ctor({Parameters(constructor, nullability)}) {Access(constructor)}");
            }

            var accessors = new HashSet<MethodInfo>();
            foreach (var property in type.GetProperties(Declared))
            {
                var getter = property.GetMethod;
                var setter = property.SetMethod;
                if (getter is not null)
                {
                    accessors.Add(getter);
                }

                if (setter is not null)
                {
                    accessors.Add(setter);
                }

                if (!IsVisible(getter) && !IsVisible(setter))
                {
                    continue;
                }

                var parts = new List<string>();
                if (getter is not null)
                {
                    parts.Add(Access(getter) + " get");
                }

                if (setter is not null)
                {
                    parts.Add(Access(setter) + (IsInit(setter) ? " init" : " set"));
                }

                var any = getter ?? setter!;
                var flags = new List<string>();
                if (any.IsStatic)
                {
                    flags.Add("static");
                }

                if (property.IsDefined(typeof(RequiredMemberAttribute), inherit: false))
                {
                    flags.Add("required");
                }

                if (Overriding(any) is { } overriding)
                {
                    flags.Add(overriding);
                }

                var index = property.GetIndexParameters().Length == 0
                    ? string.Empty
                    : "[" + string.Join(", ", property.GetIndexParameters().Select(p => Parameter(p, nullability))) + "]";

                lines.Add(
                    $"{owner}.{property.Name}{index}: {TypeName(property.PropertyType, nullability.Create(property))} "
                    + "{ " + string.Join("; ", parts) + "; }"
                    + (flags.Count == 0 ? string.Empty : " " + string.Join(" ", flags)));
            }

            foreach (var method in type.GetMethods(Declared))
            {
                if (accessors.Contains(method) || !IsVisible(method))
                {
                    continue;
                }

                var generic = method.IsGenericMethodDefinition
                    ? "<" + string.Join(", ", method.GetGenericArguments().Select(a => a.Name)) + ">"
                    : string.Empty;
                var flags = new List<string> { Access(method) };
                if (method.IsStatic)
                {
                    flags.Add("static");
                }

                if (Overriding(method) is { } overriding)
                {
                    flags.Add(overriding);
                }

                lines.Add(
                    $"{owner}.{method.Name}{generic}({Parameters(method, nullability)}): "
                    + $"{TypeName(method.ReturnType, nullability.Create(method.ReturnParameter))} {string.Join(" ", flags)}");
            }

            foreach (var field in type.GetFields(Declared))
            {
                if (!(field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly))
                {
                    continue;
                }

                var flags = new List<string> { FieldAccess(field) };
                if (field.IsLiteral)
                {
                    flags.Add("const");
                }
                else if (field.IsStatic)
                {
                    flags.Add("static");
                }

                if (field.IsInitOnly)
                {
                    flags.Add("readonly");
                }

                lines.Add($"{owner}.{field.Name}: {TypeName(field.FieldType, nullability.Create(field))} {string.Join(" ", flags)}");
            }

            foreach (var @event in type.GetEvents(Declared))
            {
                lines.Add($"{owner}.{@event.Name}: event {TypeName(@event.EventHandlerType!)}");
            }
        }

        lines.Sort(StringComparer.Ordinal);
        return lines;
    }

    private static string TypeShape(Type type)
    {
        string kind;
        if (type.IsEnum)
        {
            kind = "enum : " + TypeName(Enum.GetUnderlyingType(type));
        }
        else if (type.IsInterface)
        {
            kind = "interface";
        }
        else if (type.IsValueType)
        {
            kind = (type.IsDefined(typeof(IsReadOnlyAttribute), inherit: false) ? "readonly " : string.Empty) + "struct";
        }
        else if (type.IsAbstract && type.IsSealed)
        {
            kind = "static class";
        }
        else
        {
            kind = (type.IsAbstract ? "abstract " : string.Empty) + (type.IsSealed ? "sealed " : string.Empty) + "class";
            if (type.BaseType is { } baseType && baseType != typeof(object))
            {
                kind += " : " + TypeName(baseType);
            }
        }

        // Only the interfaces this type adds itself: those its base brings are the base's surface,
        // and the ones System.Enum brings belong to the runtime and could move with an update.
        var inherited = type.BaseType?.GetInterfaces() ?? [];
        var interfaces = type.GetInterfaces()
            .Except(inherited)
            .Select(i => TypeName(i))
            .Order(StringComparer.Ordinal)
            .ToList();
        return interfaces.Count == 0 ? kind : kind + " implements " + string.Join(", ", interfaces);
    }

    private static bool IsVisible(MethodBase? method) =>
        method is not null && (method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly);

    private static bool IsInit(MethodInfo setter) =>
        setter.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit));

    private static string? Overriding(MethodInfo method)
    {
        if (method.IsAbstract)
        {
            return "abstract";
        }

        if (!method.IsVirtual)
        {
            return null;
        }

        var overrides = method.GetBaseDefinition().DeclaringType != method.DeclaringType;
        return method.IsFinal
            ? overrides ? "sealed override" : null
            : overrides ? "override" : "virtual";
    }

    private static string Access(MethodBase method) =>
        method.IsPublic ? "public"
        : method.IsFamilyOrAssembly ? "protected internal"
        : method.IsFamily ? "protected"
        : method.IsFamilyAndAssembly ? "private protected"
        : method.IsAssembly ? "internal"
        : "private";

    private static string FieldAccess(FieldInfo field) =>
        field.IsPublic ? "public" : field.IsFamilyOrAssembly ? "protected internal" : "protected";

    private static string Parameters(MethodBase method, NullabilityInfoContext nullability) =>
        string.Join(", ", method.GetParameters().Select(parameter => Parameter(parameter, nullability)));

    private static string Parameter(ParameterInfo parameter, NullabilityInfoContext nullability)
    {
        var text = TypeName(parameter.ParameterType, nullability.Create(parameter)) + " " + parameter.Name;
        if (parameter.HasDefaultValue)
        {
            text += " = " + Literal(parameter.DefaultValue);
        }

        return text;
    }

    private static string Literal(object? value) => value switch
    {
        null => "null",
        string text => "\"" + text + "\"",
        bool flag => flag ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "null",
    };

    // A type spelled with its namespace, generic arguments in angle brackets, and the nullability
    // the compiler recorded for a reference type.
    private static string TypeName(Type type, NullabilityInfo? nullability = null)
    {
        if (type.IsByRef)
        {
            return "ref " + TypeName(type.GetElementType()!, nullability?.ElementType);
        }

        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            return TypeName(underlying, nullability?.GenericTypeArguments.FirstOrDefault()) + "?";
        }

        string name;
        if (type.IsGenericParameter)
        {
            // Spelled as declared: an unconstrained type parameter reads as possibly null to the
            // nullability API whether or not it was annotated, so no annotation is reported for it.
            return type.Name;
        }
        else if (type.IsArray)
        {
            name = TypeName(type.GetElementType()!, nullability?.ElementType) + "[]";
        }
        else if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            var bare = definition.FullName ?? definition.Name;
            bare = bare[..bare.IndexOf('`', StringComparison.Ordinal)];
            var arguments = type.GetGenericArguments();
            var builder = new StringBuilder(bare).Append('<');
            for (var i = 0; i < arguments.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                var argumentNullability = nullability is not null && i < nullability.GenericTypeArguments.Length
                    ? nullability.GenericTypeArguments[i]
                    : null;
                builder.Append(TypeName(arguments[i], argumentNullability));
            }

            name = builder.Append('>').ToString();
        }
        else
        {
            name = (type.FullName ?? type.Name).Replace('+', '.');
        }

        var nullable = !type.IsValueType
            && nullability is not null
            && (nullability.ReadState == NullabilityState.Nullable || nullability.WriteState == NullabilityState.Nullable);
        return nullable ? name + "?" : name;
    }
}
