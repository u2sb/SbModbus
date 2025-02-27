using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

using static SbModbus.SourceGenerator.Utils;

namespace SbModbus.SourceGenerator;

public static class SbModbusStructGenerator
{
  private const string SbModbusStructAttributeName = "SbModbus.Attributes.SbModbusStructAttribute";
  private const string FieldOffsetAttributeName = "System.Runtime.InteropServices.FieldOffsetAttribute";


  public static void Gen(GeneratorExecutionContext context, INamedTypeSymbol structSymbol)
  {
    var sbModbusAttr = structSymbol.GetAttributes().FirstOrDefault(attr =>
      attr.AttributeClass != null && attr.AttributeClass.ToDisplayString() == SbModbusStructAttributeName);

    if (sbModbusAttr == null) return;

    var encodingMode = GetEncodingMode(sbModbusAttr);
    var fieldInfos = CollectFieldData(structSymbol).ToList();

    if (fieldInfos.Count == 0) return;

    var source = GenerateCodeForStruct(structSymbol, encodingMode, fieldInfos);
    context.AddSource($"{structSymbol.Name}_ModbusStruct.g.cs", SourceText.From(source, Encoding.UTF8));
  }

  private static string GenerateCodeForStruct(INamedTypeSymbol structSymbol, byte encodingMode,
    List<FieldInfo> fieldInfos)
  {
    var structName = structSymbol.Name;
    var isGlobalNamespace = structSymbol.ContainingNamespace.IsGlobalNamespace;
    var namespaceName = structSymbol.ContainingNamespace.ToDisplayString();

    var toTStringBuilder = new StringBuilder();
    var toBytesStringBuilder = new StringBuilder();

    foreach (var fieldInfo in fieldInfos)
    {
      toTStringBuilder.AppendLine(BitConverterToTString(fieldInfo));
      toBytesStringBuilder.AppendLine(BitConverterToBytesString(fieldInfo));
    }


    var sb = new StringBuilder();
    sb.AppendLine("// Auto-generated code");
    sb.AppendLine("#pragma warning disable");
    sb.AppendLine("using SbModbus.Utils;");
    sb.AppendLine("using System;");
    sb.AppendLine("using System.Runtime.CompilerServices;");
    sb.AppendLine("using static SbModbus.Utils.Utils;");

    if (!isGlobalNamespace)
    {
      sb.AppendLine($"namespace {namespaceName}");
      sb.AppendLine("{");
    }

    sb.AppendLine($"partial struct {structName}");
    sb.AppendLine("{");
    sb.AppendLine($"public {structName}(ReadOnlySpan<byte> data, byte mode = {encodingMode})");
    sb.AppendLine("{");
    sb.AppendLine($"CheckLength(data, Unsafe.SizeOf<{structName}>());");
    sb.AppendLine($"{toTStringBuilder}");
    sb.AppendLine("}");
    sb.AppendLine($"public {structName}(ReadOnlySpan<ushort> data0, byte mode = {encodingMode})");
    sb.AppendLine("{");
    sb.AppendLine("var data = data0.AsReadOnlyByteSpan();");
    sb.AppendLine($"CheckLength(data, Unsafe.SizeOf<{structName}>());");
    sb.AppendLine($"{toTStringBuilder}");
    sb.AppendLine("}");
    sb.AppendLine($"public byte[] ToBytes(byte mode = {encodingMode})");
    sb.AppendLine("{");
    sb.AppendLine($"var data = new byte[Unsafe.SizeOf<{structName}>()];");
    sb.AppendLine("var span = data.AsSpan();");
    sb.AppendLine("WriteTo(span, mode);");
    sb.AppendLine("return data;");
    sb.AppendLine("}");
    sb.AppendLine($"public void WriteTo(Span<byte> span, byte mode = {encodingMode})");
    sb.AppendLine("{");
    sb.AppendLine($"CheckLength(span, Unsafe.SizeOf<{structName}>());");
    sb.AppendLine($"{toBytesStringBuilder}");
    sb.AppendLine("}");
    
    sb.AppendLine($"public Span<byte> AsSpan()");
    sb.AppendLine("{");
    sb.AppendLine($"return this.AsByteSpan();");
    sb.AppendLine("}");
    
    sb.AppendLine($"public Span<byte> Slice(int start, int length)");
    sb.AppendLine("{");
    sb.AppendLine("var span = AsSpan();");
    sb.AppendLine("return span.Slice(start, length);");
    sb.AppendLine("}");

    sb.AppendLine($"public Span<byte> this[Range range]");
    sb.AppendLine("{");
    sb.AppendLine("get");
    sb.AppendLine("{");
    sb.AppendLine("var span = AsSpan();");
    sb.AppendLine("return span[range];");
    sb.AppendLine("}");
    sb.AppendLine("}");
    
    sb.AppendLine("}");
    if (!isGlobalNamespace) sb.AppendLine("}");

    return sb.ToString();
  }

  private static string BitConverterToTString(FieldInfo fieldInfo)
  {
    var size = SizeOfType(fieldInfo.Type);
    return size switch
    {
      0 =>
        $"this.{fieldInfo.Name} = new {fieldInfo.Type.ToDisplayString()}(data.Slice({fieldInfo.Offset}, Unsafe.SizeOf<{fieldInfo.Type.ToDisplayString()}>()), mode);",
      1 or 2 or 4 or 8 =>
        $"this.{fieldInfo.Name} = data[{fieldInfo.Offset}..{fieldInfo.Offset + size}].ToT<{fieldInfo.Type.ToDisplayString()}>(mode);",
      _ => string.Empty
    };
  }

  private static string BitConverterToBytesString(FieldInfo fieldInfo)
  {
    var size = SizeOfType(fieldInfo.Type);

    return size switch
    {
      0 =>
        $"this.{fieldInfo.Name}.WriteTo(span.Slice({fieldInfo.Offset}, Unsafe.SizeOf<{fieldInfo.Type.ToDisplayString()}>()), mode);",
      1 or 2 or 4 or 8 =>
        $"this.{fieldInfo.Name}.WriteTo<{fieldInfo.Type.ToDisplayString()}>(span[{fieldInfo.Offset}..{fieldInfo.Offset + size}], mode);",
      _ => string.Empty
    };
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

internal class FieldInfo(string name, ITypeSymbol type, int offset, bool isPropertyBackingField)
{
  public string Name { get; } = name;

  public ITypeSymbol Type { get; } = type;

  public int Offset { get; } = offset;

  public bool IsPropertyBackingField { get; } = isPropertyBackingField;
}