using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;

// An ILC mstat file (version 2) is an ECMA-335 assembly. The bodies of its methods (Types, Methods, Blobs, RvaFields,
// FrozenObjects and others) are IL streams: each record is `ldtoken <type|method|field>` or `ldstr <blob name>`, followed
// by `ldc.i4` operands, the first of which is the node's size. It prints one record per line:
// kind<TAB>size<TAB>owner<TAB>name, where owner is the assembly that defines the record's type or member ("<mstat>" for
// the probe's own definitions, empty for blobs). runic-sdk's tests/scaling/aot-size/MstatReader with an owner column.
//   dotnet tests/aot-size/MstatReader/bin/Release/net10.0/MstatReader.dll <file.mstat>
if (args is not [var path])
{
    Console.Error.WriteLine("Usage: MstatReader <file.mstat>");
    return 2;
}

using var pe = new PEReader(File.OpenRead(path));
var md = pe.GetMetadataReader();
var names = new NameProvider(md);
using var output = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = false };
foreach (var typeHandle in md.TypeDefinitions)
foreach (var methodHandle in md.GetTypeDefinition(typeHandle).GetMethods())
{
    var method = md.GetMethodDefinition(methodHandle);
    var kind = md.GetString(method.Name);
    if (method.RelativeVirtualAddress == 0) continue;
    var il = pe.GetMethodBody(method.RelativeVirtualAddress).GetILReader();
    var items = new List<(char Op, int Value)>();
    var ok = true;
    while (il.RemainingBytes > 0 && ok)
    {
        var op = il.ReadByte();
        switch (op)
        {
            case 0xD0: items.Add(('T', il.ReadInt32())); break;
            case 0x72: items.Add(('S', il.ReadInt32())); break;
            case 0x20: items.Add(('I', il.ReadInt32())); break;
            case 0x1F: items.Add(('I', il.ReadSByte())); break;
            case >= 0x16 and <= 0x1E: items.Add(('I', op - 0x16)); break;
            case 0x15: items.Add(('I', -1)); break;
            case 0x26 or 0x2A or 0x00: break;
            default: ok = false; break; // not a record list
        }
    }
    if (!ok) continue;
    for (var i = 0; i < items.Count; i++)
    {
        if (items[i].Op == 'I') continue;
        if (i + 1 >= items.Count || items[i + 1].Op != 'I') continue;
        var size = items[i + 1].Value;
        string label, owner;
        if (items[i].Op == 'S')
        {
            label = md.GetUserString(MetadataTokens.UserStringHandle(items[i].Value & 0xFFFFFF));
            owner = "";
        }
        else
        {
            label = names.Token(items[i].Value);
            owner = names.Owner(items[i].Value);
        }
        output.Write(kind); output.Write('\t'); output.Write(size); output.Write('\t');
        output.Write(owner); output.Write('\t'); output.WriteLine(label.Replace('\t', ' ').Replace('\n', ' '));
    }
}
output.Flush();
return 0;

sealed partial class NameProvider(MetadataReader md) : ISignatureTypeProvider<string, object?>
{
    public string Owner(int token)
    {
        var handle = MetadataTokens.EntityHandle(token);
        try
        {
            return handle.Kind switch
            {
                HandleKind.MethodDefinition => "?",
                HandleKind.MemberReference => OwnerOf(md.GetMemberReference((MemberReferenceHandle)handle).Parent),
                HandleKind.MethodSpecification => OwnerOfMethod(md.GetMethodSpecification((MethodSpecificationHandle)handle).Method),
                _ => OwnerOf(handle),
            };
        }
        catch { return "?"; }
    }

    private string OwnerOfMethod(EntityHandle h) => h.Kind == HandleKind.MemberReference
        ? OwnerOf(md.GetMemberReference((MemberReferenceHandle)h).Parent) : "?";

    private string OwnerOf(EntityHandle h)
    {
        switch (h.Kind)
        {
            case HandleKind.TypeReference:
                var tr = md.GetTypeReference((TypeReferenceHandle)h);
                return tr.ResolutionScope.Kind switch
                {
                    HandleKind.AssemblyReference => md.GetString(md.GetAssemblyReference((AssemblyReferenceHandle)tr.ResolutionScope).Name),
                    HandleKind.TypeReference => OwnerOf((TypeReferenceHandle)tr.ResolutionScope),
                    HandleKind.ModuleReference => md.GetString(md.GetModuleReference((ModuleReferenceHandle)tr.ResolutionScope).Name),
                    _ => "?",
                };
            case HandleKind.TypeSpecification:
                var blob = md.GetBlobReader(md.GetTypeSpecification((TypeSpecificationHandle)h).Signature);
                var code = blob.ReadSignatureHeader();
                blob = md.GetBlobReader(md.GetTypeSpecification((TypeSpecificationHandle)h).Signature);
                return OwnerFromSig(ref blob);
            case HandleKind.TypeDefinition: return "<mstat>";
            default: return "?";
        }
    }

    private string OwnerFromSig(ref BlobReader b)
    {
        var t = (SignatureTypeCode)b.ReadByte();
        switch (t)
        {
            case SignatureTypeCode.GenericTypeInstance:
                b.ReadByte();
                return OwnerOf(b.ReadTypeHandle());
            case SignatureTypeCode.SZArray:
            case SignatureTypeCode.Pointer:
            case SignatureTypeCode.ByReference:
                return OwnerFromSig(ref b);
            case SignatureTypeCode.Array:
                return OwnerFromSig(ref b);
            case (SignatureTypeCode)0x11: // valuetype
            case (SignatureTypeCode)0x12: // class
                return OwnerOf(b.ReadTypeHandle());
            default:
                return "System.Private.CoreLib";
        }
    }

    public string Token(int token)
    {
        var handle = MetadataTokens.EntityHandle(token);
        return handle.Kind switch
        {
            HandleKind.MethodDefinition => Method((MethodDefinitionHandle)handle),
            HandleKind.MemberReference => Member((MemberReferenceHandle)handle),
            HandleKind.MethodSpecification => Specification((MethodSpecificationHandle)handle),
            _ => Type(handle),
        };
    }

    private string Type(EntityHandle handle) => handle.Kind switch
    {
        HandleKind.TypeDefinition => GetTypeFromDefinition(md, (TypeDefinitionHandle)handle, 0),
        HandleKind.TypeReference => GetTypeFromReference(md, (TypeReferenceHandle)handle, 0),
        HandleKind.TypeSpecification => md.GetTypeSpecification((TypeSpecificationHandle)handle).DecodeSignature(this, null),
        _ => handle.Kind.ToString(),
    };

    private string Method(MethodDefinitionHandle handle)
    {
        var method = md.GetMethodDefinition(handle);
        return Type(method.GetDeclaringType()) + "." + md.GetString(method.Name);
    }

    private string Member(MemberReferenceHandle handle)
    {
        var member = md.GetMemberReference(handle);
        return Type(member.Parent) + "." + md.GetString(member.Name);
    }

    private string Specification(MethodSpecificationHandle handle)
    {
        var specification = md.GetMethodSpecification(handle);
        var target = specification.Method.Kind == HandleKind.MethodDefinition
            ? Method((MethodDefinitionHandle)specification.Method)
            : Member((MemberReferenceHandle)specification.Method);
        return target + "<" + string.Join(", ", specification.DecodeSignature(this, null)) + ">";
    }

    [GeneratedRegex("`\\d+")]
    private static partial Regex Arity();

    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) =>
        Arity().Replace(genericType, "") + "<" + string.Join(", ", typeArguments) + ">";
    public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[" + new string(',', shape.Rank - 1) + "]";
    public string GetSZArrayType(string elementType) => elementType + "[]";
    public string GetByReferenceType(string elementType) => elementType + "&";
    public string GetPointerType(string elementType) => elementType + "*";
    public string GetPinnedType(string elementType) => elementType;
    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
    public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr";
    public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;
    public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;
    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => "System." + typeCode;

    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
    {
        var type = reader.GetTypeDefinition(handle);
        if (type.GetDeclaringType() is { IsNil: false } declaring)
            return GetTypeFromDefinition(reader, declaring, rawTypeKind) + "+" + reader.GetString(type.Name);
        var ns = reader.GetString(type.Namespace);
        return ns.Length > 0 ? ns + "." + reader.GetString(type.Name) : reader.GetString(type.Name);
    }

    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        var type = reader.GetTypeReference(handle);
        if (type.ResolutionScope.Kind == HandleKind.TypeReference)
            return GetTypeFromReference(reader, (TypeReferenceHandle)type.ResolutionScope, rawTypeKind) + "+" + reader.GetString(type.Name);
        var ns = reader.GetString(type.Namespace);
        return ns.Length > 0 ? ns + "." + reader.GetString(type.Name) : reader.GetString(type.Name);
    }

    public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
        reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
}
