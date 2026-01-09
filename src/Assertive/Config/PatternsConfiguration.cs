using System;
using Assertive.Plugin;

namespace Assertive.Config
{
  public static partial class Configuration
  {
    /// <summary>
    /// Configuration for custom assertion patterns.
    /// </summary>
    public class PatternsConfiguration
    {
      /// <summary>
      /// Register a custom pattern.
      /// </summary>
      /// <param name="pattern">The pattern definition.</param>
      /// <example>
      /// Configuration.Patterns.Register(new PatternDefinition
      /// {
      ///   Match = [new MatchPredicate { Method = new MethodMatch { Name = "None" } }],
      ///   AllowNegation = true,
      ///   Output = new OutputDefinition
      ///   {
      ///     Expected = "Collection {instance} should not contain any items.",
      ///     Actual = "It contained {instance.count} items."
      ///   },
      ///   OutputWhenNegated = new OutputDefinition
      ///   {
      ///     Expected = "Collection {instance} should contain at least one item.",
      ///     Actual = "It was empty."
      ///   }
      /// });
      /// </example>
      public void Register(PatternDefinition pattern)
      {
        if (pattern.AllowNegation && pattern.OutputWhenNegated == null)
        {
          throw new ArgumentException(
            "OutputWhenNegated must be provided when AllowNegation is true.",
            nameof(pattern));
        }

        CustomPatternRegistry.Register(pattern);
      }

      /// <summary>
      /// Remove all registered custom patterns.
      /// </summary>
      public void Clear()
      {
        CustomPatternRegistry.Clear();
      }
    }

    /// <summary>
    /// Custom assertion pattern configuration.
    /// Use this to register patterns that provide friendly messages for custom assertion methods.
    /// </summary>
    public static PatternsConfiguration Patterns { get; } = new();
  }
}
