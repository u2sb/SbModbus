using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace SbModbus.SourceGenerator
{
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
      if (!(context.SyntaxReceiver is SbModbusStructSyntaxReceiver receiver)) return;

      foreach (var structDecl in receiver.Structs)
      {
        var model = context.Compilation.GetSemanticModel(structDecl.SyntaxTree);
        if (!(model.GetDeclaredSymbol(structDecl) is INamedTypeSymbol structSymbol)) continue;

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
        toTStringBuilder.AppendLine(BitConverterToTString(fieldInfo, encodingMode));
        toBytesStringBuilder.AppendLine(BitConverterToBytesString(fieldInfo, encodingMode));
      }

      return $@"
// Auto-generated code
namespace {namespaceName}
{{
  public partial struct {structName}
  {{
    public {structName}(ReadOnlySpan<byte> data)
    {{
{toTStringBuilder}
    }}

    public {structName}(ReadOnlySpan<ushort> data0)
    {{
      var data = SbModbus.Utils.BitConverter.AsBytes(data0);
{toTStringBuilder}
    }}

    public byte[] ToBytes()
    {{
      var data = new byte[System.Runtime.InteropServices.Marshal.SizeOf<{structName}>()];
      var span = data.AsSpan();
{toBytesStringBuilder}
      return data;
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

    private static string BitConverterToTString(FieldInfo fieldInfo, byte mode)
    {
      var size = SizeOfType(fieldInfo.Type);
      return
        $"      this.{fieldInfo.Name} = SbModbus.Utils.BitConverter.ToT<{fieldInfo.Type.ToDisplayString()}>(data[{fieldInfo.Offset}..{fieldInfo.Offset + size}], (byte){mode});";
    }

    private static string BitConverterToBytesString(FieldInfo fieldInfo, byte mode)
    {
      var size = SizeOfType(fieldInfo.Type);
      return
        $"      SbModbus.Utils.BitConverter.ToBytes<{fieldInfo.Type.ToDisplayString()}>(this.{fieldInfo.Name}, span[{fieldInfo.Offset}..{fieldInfo.Offset + size}], (byte){mode});";
    }

    private static int SizeOfType(ITypeSymbol typeSymbol)
    {
      var size = 0;
      switch (typeSymbol.SpecialType)
      {
        case SpecialType.System_Byte:
          size = 1;
          break;
        case SpecialType.System_Int16:
        case SpecialType.System_UInt16:
          size = 2;
          break;

        case SpecialType.System_Int32:
        case SpecialType.System_UInt32:
        case SpecialType.System_Single:
          size = 4;
          break;

        case SpecialType.System_Int64:
        case SpecialType.System_UInt64:
        case SpecialType.System_Double:
          size = 8;
          break;
        default:
          throw new ArgumentOutOfRangeException();
      }

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
        if (member is IFieldSymbol field && !field.IsImplicitlyDeclared)
        {
          if (GetFieldOffset(field) is int offset)
            yield return new FieldInfo(
              field.Name,
              field.Type,
              offset,
              false);
        }
        else if (member is IPropertySymbol property)
        {
          var backingField = structSymbol
            .GetMembers()
            .OfType<IFieldSymbol>()
            .FirstOrDefault(f =>
              f.IsImplicitlyDeclared &&
              f.AssociatedSymbol?.Equals(property, SymbolEqualityComparer.Default) == true);

          if (backingField != null && GetFieldOffset(backingField) is int offset)
            yield return new FieldInfo(
              property.Name,
              property.Type,
              offset,
              true);
        }
    }

    private static int? GetFieldOffset(IFieldSymbol field)
    {
      if (field.GetAttributes().FirstOrDefault(attr =>
            attr.AttributeClass != null &&
            attr.AttributeClass.ToDisplayString() == FieldOffsetAttributeName) is
          AttributeData attr1)
        return attr1.ConstructorArguments[0].Value as int?;

      return null;
    }
  }

  internal class SbModbusStructSyntaxReceiver : ISyntaxReceiver
  {
    public List<StructDeclarationSyntax> Structs { get; } = new List<StructDeclarationSyntax>();

    public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
    {
      if (syntaxNode is StructDeclarationSyntax structDecl && structDecl.AttributeLists.Count > 0)
        Structs.Add(structDecl);
    }
  }

  internal class FieldInfo
  {
    public FieldInfo(string name, ITypeSymbol type, int offset, bool isPropertyBackingField)
    {
      Name = name;
      Type = type;
      Offset = offset;
      IsPropertyBackingField = isPropertyBackingField;
    }

    public string Name { get; }

    public ITypeSymbol Type { get; }

    public int Offset { get; }

    public bool IsPropertyBackingField { get; }
  }
}