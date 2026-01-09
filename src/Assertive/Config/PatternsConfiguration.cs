using System;
using Assertive.Plugin;

namespace Assertive.Config
{
  public static partial class Configuration
  {
    /// <summary>
    /// Provides methods for registering custom assertion patterns.
    /// </summary>
    public class PatternsConfiguration
    {
      /// <summary>
      /// Registers a custom pattern that provides friendly error messages for specific assertions.
      /// </summary>
      /// <param name="pattern">The pattern definition.</param>
      /// <exception cref="ArgumentException">
      /// Thrown when <see cref="PatternDefinition.AllowNegation"/> is true
      /// but <see cref="PatternDefinition.OutputWhenNegated"/> is not provided.
      /// </exception>
      /// <example>
      /// <code>
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
      /// </code>
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
      /// Removes all registered custom patterns. Useful for test isolation.
      /// </summary>
      public void Clear()
      {
        CustomPatternRegistry.Clear();
      }
    }

    /// <summary>
    /// Custom assertion patterns that provide friendly error messages for specific method calls or property accesses.
    /// </summary>
    public static PatternsConfiguration Patterns { get; } = new();
  }
}
