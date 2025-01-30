using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace SbModbus.SourceGenerator;

[Generator]
public class ModbusStructGenerator : ISourceGenerator
{
  private const string SbModbusStructAttributeName = "SbModbus.Attributes.SbModbusStructAttribute";
  private const string FieldOffsetAttributeName = "System.Runtime.InteropServices.FieldOffsetAttribute";

  public void Initialize(GeneratorInitializationContext context)
  {
    context.RegisterForSyntaxNotifications(() => new SbModbusStructSyntaxReceiver());
  }


  public void Execute(GeneratorExecutionContext context)
  {
    if (context.SyntaxReceiver is not SbModbusStructSyntaxReceiver receiver) return;

    foreach (var structDecl in receiver.Structs)
    {
      var model = context.Compilation.GetSemanticModel(structDecl.SyntaxTree);
      if (model.GetDeclaredSymbol(structDecl) is not INamedTypeSymbol structSymbol) continue;

      var sbModbusAttr = structSymbol.GetAttributes().FirstOrDefault(attr =>
        attr.AttributeClass != null && attr.AttributeClass.ToDisplayString() == SbModbusStructAttributeName);

      if (sbModbusAttr == null) continue;

      var encodingMode = GetEncodingMode(sbModbusAttr);
      var fieldInfos = CollectFieldData(structSymbol).ToList();

      if (fieldInfos.Count == 0) continue;

      var source = GenerateCodeForStruct(structSymbol, encodingMode, fieldInfos);
      context.AddSource($"{structSymbol.Name}_ModbusStruct.g.cs", SourceText.From(source, Encoding.UTF8));
    }
  }

  private static string GenerateCodeForStruct(INamedTypeSymbol structSymbol, byte encodingMode,
    List<FieldInfo> fieldInfos)
  {
    var structName = structSymbol.Name;
    var namespaceName = structSymbol.ContainingNamespace.ToDisplayString();

    var toTStringBuilder = new StringBuilder();
    var toBytesStringBuilder = new StringBuilder();

    foreach (var fieldInfo in fieldInfos)
    {
      toTStringBuilder.AppendLine(BitConverterToTString(fieldInfo));
      toBytesStringBuilder.AppendLine(BitConverterToBytesString(fieldInfo));
    }

    return $@"
// Auto-generated code
using SbModbus.Utils;
using System.Runtime.CompilerServices;

using static SbModbus.Utils.Utils;

namespace {namespaceName}
{{
  public partial struct {structName}
  {{
    public {structName}(ReadOnlySpan<byte> data, byte mode = {encodingMode})
    {{
      CheckLength(data, Unsafe.SizeOf<{structName}>());
{toTStringBuilder}
    }}

    public {structName}(ReadOnlySpan<ushort> data0, byte mode = {encodingMode})
    {{
      var data = data0.AsBytes();
      CheckLength(data, Unsafe.SizeOf<{structName}>());
{toTStringBuilder}
    }}

    public byte[] ToBytes(byte mode = {encodingMode})
    {{
      var data = new byte[Unsafe.SizeOf<{structName}>()];
      var span = data.AsSpan();
      this.ToBytes(span, mode);
      return data;
    }}

    public void ToBytes(Span<byte> span, byte mode = {encodingMode})
    {{
      CheckLength(span, Unsafe.SizeOf<{structName}>());
{toBytesStringBuilder}
    }}

    public static implicit operator {structName}(ReadOnlySpan<byte> data)
    {{
      return new {structName}(data);
    }}

    public static implicit operator byte[]({structName} value)
    {{
      return value.ToBytes();
    }}
  }}
}}
";
  }

  private static string BitConverterToTString(FieldInfo fieldInfo)
  {
    var size = SizeOfType(fieldInfo.Type);
    return size != 0
      ? $"      this.{fieldInfo.Name} = data[{fieldInfo.Offset}..{fieldInfo.Offset + size}].ToT<{fieldInfo.Type.ToDisplayString()}>(mode);"
      : $"      this.{fieldInfo.Name} = new {fieldInfo.Type.ToDisplayString()}(data.Slice({fieldInfo.Offset}, Unsafe.SizeOf<{fieldInfo.Type.ToDisplayString()}>()), mode);";
  }

  private static string BitConverterToBytesString(FieldInfo fieldInfo)
  {
    var size = SizeOfType(fieldInfo.Type);

    return size != 0
      ? $"      this.{fieldInfo.Name}.ToBytes<{fieldInfo.Type.ToDisplayString()}>(span[{fieldInfo.Offset}..{fieldInfo.Offset + size}], mode);"
      : $"      this.{fieldInfo.Name}.ToBytes(span.Slice({fieldInfo.Offset}, Unsafe.SizeOf<{fieldInfo.Type.ToDisplayString()}>()), mode);";
  }

  private static int SizeOfType(ITypeSymbol typeSymbol)
  {
    var size = typeSymbol.SpecialType switch
    {
      SpecialType.System_Byte => 1,
      SpecialType.System_Int16 or SpecialType.System_UInt16 => 2,
      SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Single => 4,
      SpecialType.System_Int64 or SpecialType.System_UInt64 or SpecialType.System_Double => 8,
      _ => 0
    };

    return size;
  }

  /// <summary>
  ///   获取编码方式
  /// </summary>
  /// <param name="attribute"></param>
  /// <returns></returns>
  private static byte GetEncodingMode(AttributeData attribute)
  {
    if (attribute.ConstructorArguments[0].Value is byte mode) return mode;
    return 0;
  }

  /// <summary>
  ///   获取信息
  /// </summary>
  /// <param name="structSymbol"></param>
  /// <returns></returns>
  private static IEnumerable<FieldInfo> CollectFieldData(INamedTypeSymbol structSymbol)
  {
    foreach (var member in structSymbol.GetMembers())
      switch (member)
      {
        case IFieldSymbol { IsImplicitlyDeclared: false } field:
        {
          if (GetFieldOffset(field) is { } offset)
            yield return new FieldInfo(
              field.Name,
              field.Type,
              offset,
              false);
          break;
        }
        case IPropertySymbol property:
        {
          var backingField = structSymbol
            .GetMembers()
            .OfType<IFieldSymbol>()
            .FirstOrDefault(f =>
              f.IsImplicitlyDeclared &&
              f.AssociatedSymbol?.Equals(property, SymbolEqualityComparer.Default) == true);

          if (backingField != null && GetFieldOffset(backingField) is { } offset)
            yield return new FieldInfo(
              property.Name,
              property.Type,
              offset,
              true);
          break;
        }
      }
  }

  private static int? GetFieldOffset(IFieldSymbol field)
  {
    if (field.GetAttributes().FirstOrDefault(attr =>
          attr.AttributeClass != null &&
          attr.AttributeClass.ToDisplayString() == FieldOffsetAttributeName) is { } attr1)
      return attr1.ConstructorArguments[0].Value as int?;

    return null;
  }
}

internal class SbModbusStructSyntaxReceiver : ISyntaxReceiver
{
  public List<StructDeclarationSyntax> Structs { get; } = new();

  public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
  {
    if (syntaxNode is StructDeclarationSyntax { AttributeLists.Count: > 0 } structDecl)
      Structs.Add(structDecl);
  }
}

internal class FieldInfo(string name, ITypeSymbol type, int offset, bool isPropertyBackingField)
{
  public string Name { get; } = name;

  public ITypeSymbol Type { get; } = type;

  public int Offset { get; } = offset;

  public bool IsPropertyBackingField { get; } = isPropertyBackingField;
}