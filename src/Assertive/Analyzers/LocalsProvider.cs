using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Assertive.Expressions;
using Assertive.Helpers;

namespace Assertive.Analyzers
{
  internal static class LocalsProvider
  {
    /// <summary>
    /// Gets the local variables used in an expression.
    /// </summary>
    /// <returns>A list of local variables, or null if there are none or an error occurred.</returns>
    public static List<LocalVariable>? GetLocals(Expression expression, HashSet<Expression> evaluatedExpressions)
    {
      try
      {
        var visitor = new LocalsExpressionVisitor();

        visitor.Visit(expression);

        var locals = visitor.Locals;

        if (!locals.Any())
        {
          return null;
        }

        var localVariables = new List<LocalVariable>();

        foreach (var local in locals.Values)
        {
          if (evaluatedExpressions.Contains(local.Expression))
          {
            continue;
          }

          var value = Serializer.Serialize(ExpressionHelper.EvaluateExpression(local.Expression));
          localVariables.Add(new LocalVariable(local.Name, value));
        }

        if (localVariables.Count == 0)
        {
          return null;
        }

        return localVariables;
      }
      catch
      {
        return null;
      }
    }

    /// <summary>
    /// Gets the local variables formatted as a string (for backward compatibility).
    /// </summary>
    [Obsolete("Use GetLocals() instead for structured data")]
    public static string? LocalsToString(Expression expression, HashSet<Expression> evaluatedExpressions)
    {
      var locals = GetLocals(expression, evaluatedExpressions);

      if (locals == null)
      {
        return null;
      }

      var lines = locals.Select(local => $"- {local.Name} = {local.Value}");
      return string.Join(Environment.NewLine, lines);
    }
  }
}