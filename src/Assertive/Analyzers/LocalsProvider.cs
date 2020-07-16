using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using Assertive.Expressions;
using Assertive.Helpers;

namespace Assertive.Analyzers
{
  internal static class LocalsProvider
  {
    public static string? LocalsToString(Expression expression, HashSet<Expression> evaluatedExpressions)
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

        var localsToString = new List<string>();

        foreach (var local in locals)
        {
          if (evaluatedExpressions.Contains(local.Expression))
          {
            continue;
          }
          
          localsToString.Add(
            $"- {local.Name} = {Serializer.Serialize(ExpressionHelper.EvaluateExpression(local.Expression))}");
        }

        if (localsToString.Count == 0)
        {
          return null;
        }

        return string.Join(Environment.NewLine, localsToString);
      }
      catch
      {
        return "<exception gathering locals>";
      }
    }
  }
}