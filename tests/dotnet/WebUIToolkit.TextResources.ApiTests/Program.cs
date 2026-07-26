using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using WebUIToolkit.TextResources;

namespace WebUIToolkit.TextResources.ApiTests;

internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            string root = FindRepositoryRoot(AppContext.BaseDirectory);
            bool write = args.Length == 1 && string.Equals(args[0], "--write-baselines", StringComparison.Ordinal);
            if (args.Length > (write ? 1 : 0))
            {
                throw new InvalidOperationException("Usage: WebUIToolkit.TextResources.ApiTests [--write-baselines]");
            }

            AssertFormatterContracts();
            (Assembly Assembly, string ProjectDirectory)[] targets =
            {
                (typeof(TextResourcesCompatibility).Assembly, "WebUIToolkit.TextResources"),
                (typeof(Compiler.TextResourceCompiler).Assembly, "WebUIToolkit.TextResources.Compiler"),
                (LoadGeneratorAssembly(root), "WebUIToolkit.TextResources.Generator"),
            };
            int totalTypes = 0;
            int totalMembers = 0;
            foreach ((Assembly assembly, string projectDirectory) in targets)
            {
                ApiManifest manifest = ApiManifest.Create(assembly);
                string baselinePath = Path.Combine(root, "src", projectDirectory, "PublicAPI.Shipped.txt");
                byte[] actual = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(manifest.Text);

                if (write)
                {
                    File.WriteAllBytes(baselinePath, actual);
                }
                else
                {
                    AssertBaseline(baselinePath, actual);
                }

                totalTypes += manifest.TypeCount;
                totalMembers += manifest.MemberCount;
                Console.WriteLine($"PASS {assembly.GetName().Name}: {manifest.TypeCount} types, {manifest.MemberCount} members");
            }

            Console.WriteLine($"PASS API approval: {totalTypes} types, {totalMembers} members");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("FAIL API approval");
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static void AssertFormatterContracts()
    {
        string manifest = ApiManifest.CreateForTypes(
            typeof(ApprovalReadonlyFixture),
            typeof(ApprovalGenericFixture<>),
            typeof(ApprovalRefFixture)).Text;
        string[] requiredFragments =
        {
            "type readonly struct WebUIToolkit.TextResources.ApiTests.ApprovalReadonlyFixture",
            "property !0 Value { get; init; }",
            "where !0 : class, new()",
            "property static ref readonly System.Int32 Current { get; }",
            "method static ref readonly System.Int32 GetCurrent()",
        };
        foreach (string fragment in requiredFragments)
        {
            if (!manifest.Contains(fragment, StringComparison.Ordinal))
                throw new InvalidOperationException("API formatter self-test failed: " + fragment);
        }
    }

    private static Assembly LoadGeneratorAssembly(string root)
    {
        string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name ?? "Release";
        string generatorPath = Path.Combine(root, "src", "WebUIToolkit.TextResources.Generator", "bin", configuration, "net10.0", "WebUIToolkit.TextResources.Generator.dll");
        string codeAnalysisPath = Path.Combine(root, ".packages", "nuget", "microsoft.codeanalysis.common", "4.14.0", "lib", "netstandard2.0", "Microsoft.CodeAnalysis.dll");
        if (!File.Exists(generatorPath) || !File.Exists(codeAnalysisPath))
        {
            throw new InvalidOperationException("Generator build output or its locked Roslyn dependency is missing.");
        }

        AssemblyLoadContext.Default.Resolving += (_, name) =>
            string.Equals(name.Name, "Microsoft.CodeAnalysis", StringComparison.Ordinal)
                ? AssemblyLoadContext.Default.LoadFromAssemblyPath(codeAnalysisPath)
                : null;
        return AssemblyLoadContext.Default.LoadFromAssemblyPath(generatorPath);
    }

    private static void AssertBaseline(string path, byte[] actual)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Missing API baseline: {path}");
        }

        byte[] expected = File.ReadAllBytes(path);
        if (expected.AsSpan().SequenceEqual(actual))
        {
            return;
        }

        string expectedText = NormalizeLineEndings(Encoding.UTF8.GetString(expected));
        string actualText = NormalizeLineEndings(Encoding.UTF8.GetString(actual));
        if (string.Equals(expectedText, actualText, StringComparison.Ordinal))
        {
            return;
        }

        int firstDifference = FirstDifferentLine(expectedText, actualText);
        throw new InvalidOperationException(
            $"API baseline differs at line {firstDifference}: {path}{Environment.NewLine}" +
            "Review the exported API and run with --write-baselines to approve intentional changes.");
    }

    private static string NormalizeLineEndings(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static int FirstDifferentLine(string left, string right)
    {
        string[] leftLines = left.Split('\n');
        string[] rightLines = right.Split('\n');
        int shared = Math.Min(leftLines.Length, rightLines.Length);
        for (int i = 0; i < shared; i++)
        {
            if (!string.Equals(leftLines[i], rightLines[i], StringComparison.Ordinal)) return i + 1;
        }

        return shared + 1;
    }

    private static string FindRepositoryRoot(string start)
    {
        DirectoryInfo? directory = new(start);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "text-resources.html")) &&
                Directory.Exists(Path.Combine(directory.FullName, "src", "WebUIToolkit.TextResources")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }
}

internal sealed class ApiManifest
{
    private ApiManifest(string text, int typeCount, int memberCount)
    {
        Text = text;
        TypeCount = typeCount;
        MemberCount = memberCount;
    }

    public string Text { get; }
    public int TypeCount { get; }
    public int MemberCount { get; }

    public static ApiManifest Create(Assembly assembly)
        => Create(assembly.GetName().Name ?? string.Empty, assembly.GetExportedTypes());

    public static ApiManifest CreateForTypes(params Type[] types) => Create("formatter-self-test", types);

    private static ApiManifest Create(string assemblyName, Type[] types)
    {
        var lines = new List<string>
        {
            "# Public API approval baseline",
            "# Assembly: " + assemblyName,
            "# Generated deterministically; intentional changes require baseline review.",
            "# Nullability metadata is intentionally normalized away; readonly/init/ref/constraints and contract attributes are retained.",
            string.Empty,
        };
        int memberCount = 0;

        Array.Sort(types, (left, right) => StringComparer.Ordinal.Compare(TypeName(left), TypeName(right)));
        foreach (Type type in types)
        {
            lines.Add(TypeDeclaration(type));
            string[] members = DeclaredMembers(type).OrderBy(static value => value, StringComparer.Ordinal).ToArray();
            foreach (string member in members) lines.Add("  " + member);
            memberCount += members.Length;
            lines.Add(string.Empty);
        }

        return new ApiManifest(string.Join("\n", lines), types.Length, memberCount);
    }

    private static IEnumerable<string> DeclaredMembers(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        foreach (ConstructorInfo constructor in type.GetConstructors(flags))
        {
            yield return "ctor " + TypeName(type) + "(" + Parameters(constructor.GetParameters()) + ")" + Attributes(constructor);
        }

        foreach (PropertyInfo property in type.GetProperties(flags))
        {
            MethodInfo? getter = property.GetMethod;
            MethodInfo? setter = property.SetMethod;
            if (getter?.IsPublic != true && setter?.IsPublic != true) continue;
            string index = property.GetIndexParameters().Length == 0 ? string.Empty : "[" + Parameters(property.GetIndexParameters()) + "]";
            string setKeyword = setter is not null && IsInitOnly(setter) ? "init; " : "set; ";
            string accessors = (getter?.IsPublic == true ? "get; " : string.Empty) + (setter?.IsPublic == true ? setKeyword : string.Empty);
            yield return "property " + Static(getter ?? setter!) + PropertyType(property, getter) + " " + property.Name + index + " { " + accessors + "}" + Attributes(property);
        }

        foreach (FieldInfo field in type.GetFields(flags))
        {
            string modifiers = field.IsLiteral ? "const " : Static(field) + (field.IsInitOnly ? "readonly " : string.Empty);
            string value = field.IsLiteral ? " = " + Constant(field.GetRawConstantValue()) : string.Empty;
            yield return "field " + modifiers + TypeName(field.FieldType) + " " + field.Name + value + Attributes(field);
        }

        foreach (EventInfo @event in type.GetEvents(flags))
        {
            MethodInfo? accessor = @event.AddMethod ?? @event.RemoveMethod;
            if (accessor?.IsPublic != true) continue;
            yield return "event " + Static(accessor) + TypeName(@event.EventHandlerType!) + " " + @event.Name + Attributes(@event);
        }

        foreach (MethodInfo method in type.GetMethods(flags))
        {
            if (method.IsSpecialName && !method.Name.StartsWith("op_", StringComparison.Ordinal)) continue;
            string generic = method.IsGenericMethodDefinition
                ? "<" + string.Join(",", method.GetGenericArguments().Select(static argument => argument.Name)) + ">"
                : string.Empty;
            yield return "method " + Static(method) + ReturnType(method) + " " + method.Name + generic +
                "(" + Parameters(method.GetParameters()) + ")" + GenericConstraints(method.GetGenericArguments()) + Attributes(method);
        }
    }

    private static string TypeDeclaration(Type type)
    {
        string kind;
        string modifiers = string.Empty;
        if (type.IsEnum) kind = "enum";
        else if (type.IsInterface) kind = "interface";
        else if (typeof(MulticastDelegate).IsAssignableFrom(type.BaseType)) kind = "delegate";
        else if (type.IsValueType)
        {
            kind = (IsReadOnly(type) ? "readonly " : string.Empty) + (type.IsByRefLike ? "ref " : string.Empty) + "struct";
        }
        else
        {
            kind = "class";
            if (type.IsAbstract && type.IsSealed) modifiers = "static ";
            else if (type.IsAbstract) modifiers = "abstract ";
            else if (type.IsSealed) modifiers = "sealed ";
        }

        var contracts = new List<string>();
        if (type.IsEnum) contracts.Add(TypeName(Enum.GetUnderlyingType(type)));
        else if (type.BaseType is not null && type.BaseType != typeof(object) && type.BaseType != typeof(ValueType) &&
                 type.BaseType != typeof(Enum) && type.BaseType != typeof(MulticastDelegate))
        {
            contracts.Add(TypeName(type.BaseType));
        }

        Type[] interfaces = type.GetInterfaces();
        Array.Sort(interfaces, (left, right) => StringComparer.Ordinal.Compare(TypeName(left), TypeName(right)));
        contracts.AddRange(interfaces.Select(TypeName));
        return "type " + modifiers + kind + " " + TypeName(type) +
            (contracts.Count == 0 ? string.Empty : " : " + string.Join(", ", contracts)) +
            GenericConstraints(type.GetGenericArguments()) + Attributes(type);
    }

    private static string Parameters(ParameterInfo[] parameters) =>
        string.Join(", ", parameters.Select(Parameter));

    private static string Parameter(ParameterInfo parameter)
    {
        Type type = parameter.ParameterType;
        string prefix = string.Empty;
        if (type.IsByRef)
        {
            prefix = parameter.IsOut ? "out " : parameter.IsIn ? "in " : "ref ";
            type = type.GetElementType()!;
        }

        if (parameter.GetCustomAttributesData().Any(static attribute =>
                attribute.AttributeType.FullName == "System.ParamArrayAttribute"))
        {
            prefix = "params ";
        }

        object? defaultValue = parameter.DefaultValue;
        string optional = parameter.HasDefaultValue
            ? " = " + (defaultValue is null && type.IsValueType && Nullable.GetUnderlyingType(type) is null ? "default" : Constant(defaultValue))
            : string.Empty;
        return Attributes(parameter, prefix: true) + prefix + TypeName(type) + " " + parameter.Name + optional;
    }

    private static string ReturnType(MethodInfo method)
    {
        Type type = method.ReturnType;
        if (!type.IsByRef) return Attributes(method.ReturnParameter, prefix: true) + TypeName(type);
        string modifier = HasAttribute(method.ReturnParameter, "System.Runtime.CompilerServices.IsReadOnlyAttribute")
            ? "ref readonly "
            : "ref ";
        return Attributes(method.ReturnParameter, prefix: true) + modifier + TypeName(type.GetElementType()!);
    }

    private static string PropertyType(PropertyInfo property, MethodInfo? getter)
    {
        Type type = property.PropertyType;
        if (!type.IsByRef) return TypeName(type);
        string modifier = getter is not null && HasAttribute(getter.ReturnParameter, "System.Runtime.CompilerServices.IsReadOnlyAttribute")
            ? "ref readonly "
            : "ref ";
        return modifier + TypeName(type.GetElementType()!);
    }

    private static string Static(MethodBase member) => member.IsStatic ? "static " : string.Empty;
    private static string Static(FieldInfo field) => field.IsStatic ? "static " : string.Empty;

    private static string TypeName(Type type)
    {
        if (type.IsByRef) return TypeName(type.GetElementType()!) + "&";
        if (type.IsPointer) return TypeName(type.GetElementType()!) + "*";
        if (type.IsArray) return TypeName(type.GetElementType()!) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
        if (type.IsGenericParameter) return type.DeclaringMethod is null ? "!" + type.GenericParameterPosition : "!!" + type.GenericParameterPosition;

        string name = type.IsNested
            ? TypeName(type.DeclaringType!) + "." + WithoutArity(type.Name)
            : (string.IsNullOrEmpty(type.Namespace) ? string.Empty : type.Namespace + ".") + WithoutArity(type.Name);
        if (!type.IsGenericType) return name;

        Type[] arguments = type.GetGenericArguments();
        int inheritedArguments = type.IsNested && type.DeclaringType?.IsGenericType == true
            ? type.DeclaringType.GetGenericArguments().Length
            : 0;
        return name + "<" + string.Join(",", arguments.Skip(inheritedArguments).Select(TypeName)) + ">";
    }

    private static string WithoutArity(string name)
    {
        int tick = name.IndexOf('`');
        return tick < 0 ? name : name.Substring(0, tick);
    }

    private static bool IsInitOnly(MethodInfo setter) =>
        setter.ReturnParameter.GetRequiredCustomModifiers().Any(static modifier =>
            modifier.FullName == "System.Runtime.CompilerServices.IsExternalInit");

    private static bool IsReadOnly(Type type) =>
        HasAttribute(type, "System.Runtime.CompilerServices.IsReadOnlyAttribute");

    private static bool HasAttribute(MemberInfo member, string fullName) =>
        member.GetCustomAttributesData().Any(attribute => attribute.AttributeType.FullName == fullName);

    private static bool HasAttribute(ParameterInfo parameter, string fullName) =>
        parameter.GetCustomAttributesData().Any(attribute => attribute.AttributeType.FullName == fullName);

    private static string GenericConstraints(Type[] genericArguments)
    {
        var clauses = new List<string>();
        foreach (Type argument in genericArguments.Where(static argument => argument.IsGenericParameter))
        {
            var constraints = new List<string>();
            GenericParameterAttributes special = argument.GenericParameterAttributes & GenericParameterAttributes.SpecialConstraintMask;
            if ((special & GenericParameterAttributes.ReferenceTypeConstraint) != 0) constraints.Add("class");
            if ((special & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0) constraints.Add("struct");
            constraints.AddRange(argument.GetGenericParameterConstraints()
                .Where(constraint => constraint != typeof(ValueType) ||
                    (special & GenericParameterAttributes.NotNullableValueTypeConstraint) == 0)
                .Select(TypeName)
                .OrderBy(static value => value, StringComparer.Ordinal));
            if ((special & GenericParameterAttributes.DefaultConstructorConstraint) != 0 &&
                (special & GenericParameterAttributes.NotNullableValueTypeConstraint) == 0)
            {
                constraints.Add("new()");
            }

            if (constraints.Count != 0) clauses.Add(" where " + TypeName(argument) + " : " + string.Join(", ", constraints));
        }

        return string.Concat(clauses);
    }

    private static string Attributes(MemberInfo member) => RenderAttributes(member.GetCustomAttributesData(), prefix: false);
    private static string Attributes(ParameterInfo parameter, bool prefix) => RenderAttributes(parameter.GetCustomAttributesData(), prefix);

    private static string RenderAttributes(IEnumerable<CustomAttributeData> attributes, bool prefix)
    {
        string[] rendered = attributes
            .Where(static attribute => !IgnoredAttribute(attribute.AttributeType.FullName))
            .Select(RenderAttribute)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
        if (rendered.Length == 0) return string.Empty;
        string text = "[" + string.Join(", ", rendered) + "]";
        return prefix ? text + " " : " " + text;
    }

    private static bool IgnoredAttribute(string? fullName) => fullName is
        "System.ParamArrayAttribute" or
        "System.Runtime.InteropServices.InAttribute" or
        "System.Runtime.InteropServices.OptionalAttribute" or
        "System.Runtime.InteropServices.OutAttribute" or
        "System.Runtime.CompilerServices.AsyncIteratorStateMachineAttribute" or
        "System.Runtime.CompilerServices.AsyncStateMachineAttribute" or
        "System.Runtime.CompilerServices.CompilerGeneratedAttribute" or
        "System.Runtime.CompilerServices.IsByRefLikeAttribute" or
        "System.Runtime.CompilerServices.IsReadOnlyAttribute" or
        "System.Runtime.CompilerServices.IteratorStateMachineAttribute" or
        "System.Runtime.CompilerServices.NullableAttribute" or
        "System.Runtime.CompilerServices.NullableContextAttribute" or
        "System.Diagnostics.CodeAnalysis.AllowNullAttribute" or
        "System.Diagnostics.CodeAnalysis.DisallowNullAttribute" or
        "System.Diagnostics.CodeAnalysis.MaybeNullAttribute" or
        "System.Diagnostics.CodeAnalysis.MaybeNullWhenAttribute" or
        "System.Diagnostics.CodeAnalysis.NotNullAttribute" or
        "System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute" or
        "System.Diagnostics.CodeAnalysis.NotNullWhenAttribute";

    private static string RenderAttribute(CustomAttributeData attribute)
    {
        var arguments = new List<string>();
        arguments.AddRange(attribute.ConstructorArguments.Select(RenderAttributeArgument));
        arguments.AddRange(attribute.NamedArguments
            .OrderBy(static argument => argument.MemberName, StringComparer.Ordinal)
            .Select(static argument => argument.MemberName + " = " + RenderAttributeArgument(argument.TypedValue)));
        return TypeName(attribute.AttributeType) + (arguments.Count == 0 ? string.Empty : "(" + string.Join(", ", arguments) + ")");
    }

    private static string RenderAttributeArgument(CustomAttributeTypedArgument argument)
    {
        if (argument.ArgumentType.IsArray && argument.Value is IReadOnlyCollection<CustomAttributeTypedArgument> values)
        {
            return "[" + string.Join(", ", values.Select(RenderAttributeArgument)) + "]";
        }

        if (argument.ArgumentType == typeof(Type) && argument.Value is Type type) return "typeof(" + TypeName(type) + ")";
        if (argument.ArgumentType.IsEnum && argument.Value is not null)
        {
            string? name = Enum.GetName(argument.ArgumentType, argument.Value);
            return name is null ? Constant(argument.Value) : TypeName(argument.ArgumentType) + "." + name;
        }

        return Constant(argument.Value);
    }

    private static string Constant(object? value)
    {
        if (value is null || value == DBNull.Value || value == Missing.Value) return "null";
        if (value is string text) return "\"" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
        if (value is char character) return "'" + character.ToString().Replace("'", "\\'", StringComparison.Ordinal) + "'";
        if (value is bool boolean) return boolean ? "true" : "false";
        if (value is float single) return single.ToString("R", CultureInfo.InvariantCulture);
        if (value is double @double) return @double.ToString("R", CultureInfo.InvariantCulture);
        if (value is IFormattable formattable) return formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty;
        return value.ToString() ?? string.Empty;
    }
}

internal readonly struct ApprovalReadonlyFixture
{
    public int Value { get; }
}

internal sealed class ApprovalGenericFixture<T> where T : class, new()
{
    public T Value { get; init; } = new T();
}

internal static class ApprovalRefFixture
{
    private static readonly int Value = 1;
    public static ref readonly int Current => ref Value;
    public static ref readonly int GetCurrent() => ref Value;
}
