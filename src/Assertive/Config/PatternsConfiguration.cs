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
      /// If a pattern with the same name already exists, it will be replaced.
      /// </summary>
      /// <param name="name">
      /// A unique name for this pattern. Used for identification and to enable replacement
      /// of existing patterns with the same name.
      /// </param>
      /// <param name="pattern">The pattern definition.</param>
      /// <exception cref="ArgumentException">
      /// Thrown when <paramref name="name"/> is null or empty, or when
      /// <see cref="PatternDefinition.AllowNegation"/> is true but
      /// <see cref="PatternDefinition.OutputWhenNegated"/> is not provided.
      /// </exception>
      /// <example>
      /// <code>
      /// Configuration.Patterns.Register("None", new PatternDefinition
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
      public void Register(string name, PatternDefinition pattern)
      {
        if (string.IsNullOrEmpty(name))
        {
          throw new ArgumentException("Pattern name cannot be null or empty.", nameof(name));
        }

        if (pattern.AllowNegation && pattern.OutputWhenNegated == null)
        {
          throw new ArgumentException(
            "OutputWhenNegated must be provided when AllowNegation is true.",
            nameof(pattern));
        }

        CustomPatternRegistry.Register(name, pattern);
      }

      /// <summary>
      /// Unregisters a custom pattern by name.
      /// </summary>
      /// <param name="name">The name of the pattern to remove.</param>
      /// <returns>True if the pattern was found and removed; false if no pattern with that name existed.</returns>
      public bool Unregister(string name)
      {
        return CustomPatternRegistry.Unregister(name);
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
