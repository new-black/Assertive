using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Assertive.Generators
{
  internal static partial class CallSiteAnalyzer
  {
    /// <summary>
    /// A lambda parameter of the assertion body bound to a value by the exception-step
    /// machinery (the iterated item, its index, or a filter candidate). TypedReplacement is
    /// the cast replacement expression for typed rendering (null when the parameter type
    /// cannot be named); ReflectiveReplacement is the bare bound identifier.
    /// </summary>
    internal readonly struct LambdaBinding
    {
      public LambdaBinding(string? typedReplacement, string reflectiveReplacement)
      {
        TypedReplacement = typedReplacement;
        ReflectiveReplacement = reflectiveReplacement;
      }

      public readonly string? TypedReplacement;
      public readonly string ReflectiveReplacement;
    }

    /// <summary>
    /// Walks an assertion body in evaluation order (children before self, mirroring the old
    /// ExpressionVisitor-based exception patterns) and records an ExceptionStep initializer
    /// for every sub-expression that can plausibly throw: member accesses, calls, element
    /// accesses, casts and divisions. Calls taking a lambda literal additionally get a
    /// LambdaIteration step whose inner steps bind the lambda's item/index parameters,
    /// replacing LambdaAwareExpressionVisitor.
    /// </summary>
    private sealed class ExceptionStepWalker
    {
      private const string StepType = "__ES";
      private const string StepKind = "__ESK";

      private static readonly SymbolDisplayFormat ShortTypeFormat = new SymbolDisplayFormat(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameOnly,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

      private readonly SemanticModel _model;
      private readonly OperandCompiler _compiler;
      private readonly ExpressionSyntax _body;
      private readonly CancellationToken _ct;

      public ExceptionStepWalker(SemanticModel model, OperandCompiler compiler, ExpressionSyntax body, CancellationToken ct)
      {
        _model = model;
        _compiler = compiler;
        _body = body;
        _ct = ct;
      }

      public string? CollectSource(ExpressionSyntax body)
      {
        var steps = new List<string>();

        Visit(body, null, 0, steps);

        return steps.Count == 0 ? null : ToArrayLiteral(steps);
      }

      private static string ToArrayLiteral(List<string> steps)
        => $"[{string.Join(", ", steps)}]";

      private void Visit(ExpressionSyntax node, IReadOnlyDictionary<string, LambdaBinding>? bindings, int depth, List<string> steps)
      {
        switch (node)
        {
          case ParenthesizedExpressionSyntax parenthesized:
            Visit(parenthesized.Expression, bindings, depth, steps);
            return;

          case PrefixUnaryExpressionSyntax prefix:
            Visit(prefix.Operand, bindings, depth, steps);
            return;

          case PostfixUnaryExpressionSyntax postfix:
            Visit(postfix.Operand, bindings, depth, steps);
            return;

          case BinaryExpressionSyntax binary:
            Visit(binary.Left, bindings, depth, steps);
            Visit(binary.Right, bindings, depth, steps);

            if (binary.Kind() is SyntaxKind.DivideExpression or SyntaxKind.ModuloExpression)
            {
              AddDivideStep(binary, bindings, steps);
            }

            return;

          case ConditionalExpressionSyntax conditional:
            Visit(conditional.Condition, bindings, depth, steps);
            Visit(conditional.WhenTrue, bindings, depth, steps);
            Visit(conditional.WhenFalse, bindings, depth, steps);
            return;

          case CastExpressionSyntax cast:
            Visit(cast.Expression, bindings, depth, steps);
            AddCastStep(cast, bindings, steps);
            return;

          case InvocationExpressionSyntax invocation:
            VisitInvocation(invocation, bindings, depth, steps);
            return;

          case MemberAccessExpressionSyntax memberAccess when memberAccess.IsKind(SyntaxKind.SimpleMemberAccessExpression):
            Visit(memberAccess.Expression, bindings, depth, steps);
            AddMemberStep(memberAccess, bindings, steps);
            return;

          case ElementAccessExpressionSyntax elementAccess:
            Visit(elementAccess.Expression, bindings, depth, steps);

            foreach (var argument in elementAccess.ArgumentList.Arguments)
            {
              Visit(argument.Expression, bindings, depth, steps);
            }

            AddIndexStep(elementAccess, bindings, steps);
            return;

          // `?.` short-circuits on null, and lambda interiors are only analyzed through
          // LambdaIteration steps (where their parameters can be bound).
          case ConditionalAccessExpressionSyntax:
          case AnonymousFunctionExpressionSyntax:
            return;

          case IdentifierNameSyntax:
          case LiteralExpressionSyntax:
            return;

          default:
            foreach (var child in node.ChildNodes())
            {
              VisitNested(child, bindings, depth, steps);
            }

            return;
        }
      }

      /// <summary>Descends through non-expression structure (argument lists, initializers).</summary>
      private void VisitNested(SyntaxNode node, IReadOnlyDictionary<string, LambdaBinding>? bindings, int depth, List<string> steps)
      {
        if (node is ExpressionSyntax expression)
        {
          Visit(expression, bindings, depth, steps);
          return;
        }

        foreach (var child in node.ChildNodes())
        {
          VisitNested(child, bindings, depth, steps);
        }
      }

      private void VisitInvocation(InvocationExpressionSyntax invocation, IReadOnlyDictionary<string, LambdaBinding>? bindings, int depth, List<string> steps)
      {
        var method = _model.GetSymbolInfo(invocation, _ct).Symbol as IMethodSymbol;
        var access = invocation.Expression is MemberAccessExpressionSyntax ma && ma.IsKind(SyntaxKind.SimpleMemberAccessExpression)
          ? ma
          : null;

        // A call with a lambda-literal argument: record a per-item iteration step first
        // (the old visitor also tried the lambda before the call's own checks). Only one
        // binding frame exists at runtime, so nested lambdas aren't iterated.
        if (depth == 0 && access != null && method != null)
        {
          AddLambdaIterationStep(invocation, access, bindings, steps);
        }

        if (access != null)
        {
          Visit(access.Expression, bindings, depth, steps);
        }
        else if (invocation.Expression is not SimpleNameSyntax and not MemberBindingExpressionSyntax)
        {
          Visit(invocation.Expression, bindings, depth, steps);
        }

        foreach (var argument in invocation.ArgumentList.Arguments)
        {
          if (argument.Expression is not AnonymousFunctionExpressionSyntax)
          {
            Visit(argument.Expression, bindings, depth, steps);
          }
        }

        if (method == null || access == null)
        {
          return;
        }

        if (method.IsStatic || method.ReducedFrom != null)
        {
          AddStaticCallStep(invocation, access, method, bindings, steps);
        }
        else if (access.Expression is not (ThisExpressionSyntax or BaseExpressionSyntax))
        {
          AddInstanceCallStep(invocation, access, method, bindings, steps);
        }
      }

      private void AddLambdaIterationStep(InvocationExpressionSyntax invocation, MemberAccessExpressionSyntax access,
        IReadOnlyDictionary<string, LambdaBinding>? bindings, List<string> steps)
      {
        foreach (var argument in invocation.ArgumentList.Arguments)
        {
          var lambda = GetExpressionLambda(argument.Expression);

          if (lambda == null || lambda.Value.Body == null || lambda.Value.Parameters.Count == 0)
          {
            continue;
          }

          var parameters = lambda.Value.Parameters;
          var lambdaBody = lambda.Value.Body;

          var innerBindings = new Dictionary<string, LambdaBinding>();

          if (BindParameter(parameters[0], "__i") is not { } itemBinding)
          {
            return;
          }

          innerBindings[parameters[0].Identifier.ValueText] = itemBinding;

          if (parameters.Count > 1
              && _model.GetDeclaredSymbol(parameters[1], _ct) is { Type.SpecialType: SpecialType.System_Int32 })
          {
            innerBindings[parameters[1].Identifier.ValueText] = new LambdaBinding("__j", "__j");
          }

          var collection = Eval(access.Expression, bindings);

          if (collection == null)
          {
            return;
          }

          var itemSteps = new List<string>();
          Visit(lambdaBody, innerBindings, 1, itemSteps);

          if (itemSteps.Count == 0)
          {
            return;
          }

          steps.Add($"new {StepType} {{ Kind = {StepKind}.LambdaIteration, " +
                    $"CollectionSource = {Quote(Display(access.Expression))}, " +
                    $"Collection = {collection}, " +
                    $"ItemSteps = {ToArrayLiteral(itemSteps)} }}");
          return;
        }
      }

      private LambdaBinding? BindParameter(ParameterSyntax parameter, string boundName)
      {
        if (_model.GetDeclaredSymbol(parameter, _ct) is not { } symbol)
        {
          return null;
        }

        var typed = IsUsableType(symbol.Type, _model.Compilation)
          ? $"(({symbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}){boundName})"
          : null;

        return new LambdaBinding(typed, boundName);
      }

      private void AddMemberStep(MemberAccessExpressionSyntax memberAccess, IReadOnlyDictionary<string, LambdaBinding>? bindings, List<string> steps)
      {
        var symbol = _model.GetSymbolInfo(memberAccess, _ct).Symbol;

        // Static members cannot be the cause of a NullReferenceException.
        var isInstanceMember = symbol is IFieldSymbol { IsStatic: false }
          or IPropertySymbol { IsStatic: false, IsIndexer: false };

        if (!isInstanceMember || memberAccess.Expression is ThisExpressionSyntax or BaseExpressionSyntax)
        {
          return;
        }

        var receiver = memberAccess.Expression;
        var receiverEval = Eval(receiver, bindings);

        if (receiverEval == null)
        {
          return;
        }

        var isArrayLength = memberAccess.Name.Identifier.ValueText == "Length"
          && _model.GetTypeInfo(receiver, _ct).Type is IArrayTypeSymbol;

        steps.Add($"new {StepType} {{ Kind = {StepKind}.{(isArrayLength ? "ArrayLength" : "Member")}, " +
                  $"NodeSource = {Quote(Display(memberAccess))}, " +
                  $"MemberName = {Quote(memberAccess.Name.Identifier.ValueText)}, " +
                  $"ReceiverSource = {Quote(Display(receiver))}, " +
                  $"{ReceiverLastMemberInitializer(receiver)}" +
                  $"Receiver = {receiverEval} }}");
      }

      private void AddInstanceCallStep(InvocationExpressionSyntax invocation, MemberAccessExpressionSyntax access,
        IMethodSymbol method, IReadOnlyDictionary<string, LambdaBinding>? bindings, List<string> steps)
      {
        var receiver = access.Expression;
        var receiverEval = Eval(receiver, bindings);

        if (receiverEval == null)
        {
          return;
        }

        steps.Add($"new {StepType} {{ Kind = {StepKind}.Call, " +
                  $"NodeSource = {Quote(Display(invocation))}, " +
                  $"MemberName = {Quote(method.Name)}, " +
                  $"MethodDisplay = {Quote($"{access.Name}{Display(invocation.ArgumentList)}")}, " +
                  $"ReceiverSource = {Quote(Display(receiver))}, " +
                  $"{ReceiverLastMemberInitializer(receiver)}" +
                  $"Receiver = {receiverEval}, " +
                  $"Node = {Eval(invocation, bindings) ?? "null"}, " +
                  $"{CommonCallInitializers(invocation, method, bindings)} }}");
      }

      private void AddStaticCallStep(InvocationExpressionSyntax invocation, MemberAccessExpressionSyntax access,
        IMethodSymbol method, IReadOnlyDictionary<string, LambdaBinding>? bindings, List<string> steps)
      {
        var isExtension = method.ReducedFrom != null;
        var receiverEval = isExtension ? Eval(access.Expression, bindings) : null;
        var nodeEval = Eval(invocation, bindings);

        if (nodeEval == null && receiverEval == null)
        {
          return;
        }

        steps.Add($"new {StepType} {{ Kind = {StepKind}.StaticCall, " +
                  $"NodeSource = {Quote(Display(invocation))}, " +
                  $"MemberName = {Quote(method.Name)}, " +
                  $"MethodDisplay = {Quote($"{access.Name}{Display(invocation.ArgumentList)}")}, " +
                  $"StaticTypeName = {Quote(method.ContainingType.ToDisplayString(ShortTypeFormat))}, " +
                  (isExtension ? $"ReceiverSource = {Quote(Display(access.Expression))}, Receiver = {receiverEval}, " : "") +
                  $"Node = {nodeEval ?? "null"}, " +
                  $"{CommonCallInitializers(invocation, method, bindings)} }}");
      }

      /// <summary>Initializers shared by instance and static call steps: argument metadata, parsing/LINQ flags, filter.</summary>
      private string CommonCallInitializers(InvocationExpressionSyntax invocation, IMethodSymbol method,
        IReadOnlyDictionary<string, LambdaBinding>? bindings)
      {
        var parts = new List<string>();

        var arguments = invocation.ArgumentList.Arguments;

        if (arguments.Count > 0)
        {
          var sources = new List<string>();
          var constants = new List<string>();
          var evals = new List<string>();
          var stringArgIndex = -1;

          for (var i = 0; i < arguments.Count; i++)
          {
            var expression = arguments[i].Expression;
            sources.Add(Quote(Display(expression)));
            constants.Add(IsConstantish(expression) ? "true" : "false");
            evals.Add(expression is AnonymousFunctionExpressionSyntax ? "null" : Eval(expression, bindings) ?? "null");

            if (stringArgIndex < 0 && _model.GetTypeInfo(expression, _ct).Type?.SpecialType == SpecialType.System_String)
            {
              stringArgIndex = i;
            }
          }

          // The analyzer null-guards every argument array and StringArgIndex defaults to
          // -1: emit only what deviates from the defaults.
          parts.Add($"ArgSources = [{string.Join(", ", sources)}]");

          if (constants.Any(c => c == "true"))
          {
            parts.Add($"ArgIsConstant = [{string.Join(", ", constants)}]");
          }

          if (evals.Any(e => e != "null"))
          {
            parts.Add($"Args = [{string.Join(", ", evals)}]");
          }

          if (stringArgIndex >= 0)
          {
            parts.Add($"StringArgIndex = {stringArgIndex}");
          }
        }

        if (IsParsingMethod(method))
        {
          parts.Add("IsParsingMethod = true");
          parts.Add($"ParseTargetTypeName = {Quote(ParseTargetName(method))}");
        }

        if (method.Name is "Single" or "SingleOrDefault" or "First" or "FirstOrDefault")
        {
          parts.Add("IsLinqElementMethod = true");

          var filterLambda = arguments.Count == 1 ? GetExpressionLambda(arguments[0].Expression) : null;

          if (filterLambda != null && filterLambda.Value.Body != null && filterLambda.Value.Parameters.Count == 1)
          {
            parts.Add("Filtered = true");

            var filterParameter = filterLambda.Value.Parameters[0];

            if (BindParameter(filterParameter, "__k") is { } candidateBinding)
            {
              var filterBindings = bindings != null
                ? bindings.ToDictionary(kv => kv.Key, kv => kv.Value)
                : new Dictionary<string, LambdaBinding>();

              filterBindings[filterParameter.Identifier.ValueText] = candidateBinding;

              if (_compiler.Compile(filterLambda.Value.Body, filterBindings) is { } filter)
              {
                parts.Add($"Filter = (__k, __i, __j) => (bool)(object)({filter})");
              }
            }
          }
        }

        return string.Join(", ", parts);
      }

      private void AddIndexStep(ElementAccessExpressionSyntax elementAccess, IReadOnlyDictionary<string, LambdaBinding>? bindings, List<string> steps)
      {
        if (elementAccess.ArgumentList.Arguments.Count != 1
            || elementAccess.Expression is ThisExpressionSyntax or BaseExpressionSyntax)
        {
          return;
        }

        var receiver = elementAccess.Expression;
        var index = elementAccess.ArgumentList.Arguments[0].Expression;
        var receiverType = _model.GetTypeInfo(receiver, _ct).Type;
        var receiverEval = Eval(receiver, bindings);
        var indexEval = Eval(index, bindings);

        if (receiverEval == null || indexEval == null)
        {
          return;
        }

        steps.Add($"new {StepType} {{ Kind = {StepKind}.Index, " +
                  $"NodeSource = {Quote(Display(elementAccess))}, " +
                  $"MemberName = \"get_Item\", " +
                  $"ReceiverSource = {Quote(Display(receiver))}, " +
                  $"{ReceiverLastMemberInitializer(receiver)}" +
                  $"Receiver = {receiverEval}, " +
                  $"IndexSource = {Quote(Display(index))}, " +
                  $"IndexIsConstant = {(IsConstantish(index) ? "true" : "false")}, " +
                  $"Index = {indexEval}, " +
                  $"IsArray = {(receiverType is IArrayTypeSymbol ? "true" : "false")}, " +
                  $"IsDictionary = {(IsDictionaryType(receiverType) ? "true" : "false")} }}");
      }

      private void AddCastStep(CastExpressionSyntax cast, IReadOnlyDictionary<string, LambdaBinding>? bindings, List<string> steps)
      {
        if (_model.GetTypeInfo(cast.Type, _ct).Type is not { } targetType)
        {
          return;
        }

        var conversion = _model.ClassifyConversion(cast.Expression, targetType);

        // Only reference conversions and unboxing can throw InvalidCastException.
        if (!conversion.IsUnboxing && !(conversion.IsExplicit && conversion.IsReference))
        {
          return;
        }

        var typeAccessor = _compiler.TypeAccessor(targetType);
        var operandEval = Eval(cast.Expression, bindings);

        if (typeAccessor == null || operandEval == null)
        {
          return;
        }

        steps.Add($"new {StepType} {{ Kind = {StepKind}.Cast, " +
                  $"NodeSource = {Quote(Display(cast))}, " +
                  $"ReceiverSource = {Quote(Display(cast.Expression))}, " +
                  $"Receiver = {operandEval}, " +
                  $"TargetType = {typeAccessor} }}");
      }

      private void AddDivideStep(BinaryExpressionSyntax binary, IReadOnlyDictionary<string, LambdaBinding>? bindings, List<string> steps)
      {
        var rightEval = Eval(binary.Right, bindings);

        if (rightEval == null)
        {
          return;
        }

        var kind = binary.IsKind(SyntaxKind.DivideExpression) ? "Divide" : "Modulo";

        steps.Add($"new {StepType} {{ Kind = {StepKind}.{kind}, " +
                  $"NodeSource = {Quote(Display(binary))}, " +
                  $"LeftSource = {Quote(Display(binary.Left))}, " +
                  $"RightSource = {Quote(Display(binary.Right))}, " +
                  $"RightIsConstant = {(IsConstantish(binary.Right) ? "true" : "false")}, " +
                  $"Right = {rightEval} }}");
      }

      private string? Eval(ExpressionSyntax expression, IReadOnlyDictionary<string, LambdaBinding>? bindings)
      {
        return _compiler.Compile(expression, bindings) is { } compiled
          ? $"(__i, __j) => (object)({compiled})"
          : null;
      }

      private string ReceiverLastMemberInitializer(ExpressionSyntax receiver)
      {
        var name = StripParens(receiver) switch
        {
          MemberAccessExpressionSyntax ma when ma.IsKind(SyntaxKind.SimpleMemberAccessExpression) => ma.Name.Identifier.ValueText,
          InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax ima } when ima.IsKind(SyntaxKind.SimpleMemberAccessExpression)
            => ima.Name.Identifier.ValueText,
          _ => null,
        };

        return name != null ? $"ReceiverLastMemberName = {Quote(name)}, " : "";
      }

      private static (SeparatedSyntaxList<ParameterSyntax> Parameters, ExpressionSyntax? Body)? GetExpressionLambda(ExpressionSyntax expression)
      {
        return expression switch
        {
          SimpleLambdaExpressionSyntax simple =>
            (SyntaxFactory.SeparatedList(new[] { simple.Parameter }), simple.Body as ExpressionSyntax),
          ParenthesizedLambdaExpressionSyntax parenthesized =>
            (parenthesized.ParameterList.Parameters, parenthesized.Body as ExpressionSyntax),
          _ => null,
        };
      }

      private static bool IsConstantish(ExpressionSyntax expression)
      {
        expression = StripParens(expression);

        while (expression is PrefixUnaryExpressionSyntax prefix
               && (prefix.IsKind(SyntaxKind.UnaryMinusExpression) || prefix.IsKind(SyntaxKind.UnaryPlusExpression)))
        {
          expression = StripParens(prefix.Operand);
        }

        return expression is LiteralExpressionSyntax;
      }

      private static bool IsParsingMethod(IMethodSymbol method)
      {
        if (method.Name is "Parse" or "TryParse")
        {
          return true;
        }

        return IsSystemConvert(method.ContainingType) && method.Name.StartsWith("To", System.StringComparison.Ordinal);
      }

      private static bool IsSystemConvert(INamedTypeSymbol type)
        => type is { Name: "Convert", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } };

      /// <summary>FormatExceptionPattern.GetTargetTypeName parity.</summary>
      private static string ParseTargetName(IMethodSymbol method)
      {
        if (method.Name is "Parse" or "TryParse")
        {
          return method.ContainingType.ToDisplayString(ShortTypeFormat);
        }

        if (IsSystemConvert(method.ContainingType) && method.Name.StartsWith("To", System.StringComparison.Ordinal))
        {
          var typePart = method.Name.Substring(2);

          return typePart switch
          {
            "Int32" => "int",
            "Int64" => "long",
            "Int16" => "short",
            "Double" => "double",
            "Single" => "float",
            "Decimal" => "decimal",
            "Boolean" => "bool",
            "Byte" => "byte",
            "DateTime" => "DateTime",
            _ => typePart,
          };
        }

        return method.ReturnsVoid ? "value" : method.ReturnType.ToDisplayString(ShortTypeFormat);
      }

      private static bool IsDictionaryType(ITypeSymbol? type)
      {
        if (type is not INamedTypeSymbol named)
        {
          return false;
        }

        return IsDictionaryDefinition(named.OriginalDefinition, "Dictionary")
               || named.AllInterfaces.Any(i => IsDictionaryDefinition(i.OriginalDefinition, "IDictionary"));
      }

      private static bool IsDictionaryDefinition(INamedTypeSymbol type, string name)
      {
        return type is { Arity: 2 }
               && type.Name == name
               && type.ContainingNamespace is { Name: "Generic", ContainingNamespace: { Name: "Collections", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } } };
      }

      private static string Quote(string text) => SymbolDisplay.FormatLiteral(text, quote: true);

      private static readonly SuppressionStripper _suppressionStripper = new();

      /// <summary>
      /// Displayed source with nullable-suppression operators removed (`sb!.Append(..)` is
      /// displayed as `sb.Append(..)`, matching the expression-tree rendering, which never
      /// saw the compile-time-only `!`).
      /// </summary>
      private static string Display(SyntaxNode node) => _suppressionStripper.Visit(node)?.ToString() ?? node.ToString();

      private sealed class SuppressionStripper : CSharpSyntaxRewriter
      {
        public override SyntaxNode? VisitPostfixUnaryExpression(PostfixUnaryExpressionSyntax node)
        {
          var visited = base.VisitPostfixUnaryExpression(node);

          return visited is PostfixUnaryExpressionSyntax postfix && postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression)
            ? postfix.Operand.WithTriviaFrom(postfix)
            : visited;
        }
      }
    }
  }
}
