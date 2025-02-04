using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SbModbus.SourceGenerator;

[Generator]
public class ModbusStructGenerator : ISourceGenerator
{
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

      SbModbusStructGenerator.Gen(context, structSymbol);
      SbModbusArrayGenerator.Gen(context, structSymbol);
    }
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