using System;
using System.Linq;
using Xunit;
using Xunit.Sdk;
using A = Assertive.Assert;
using static Assertive.DSL;

namespace Assertive.Test
{
  public class PublicApiTests
  {
    [Fact]
    public void AssertThat_works_if_assertion_is_true()
    {
      var a = 10;
      var b = 1;
      
      A.That(() => a * b == 10);
      A.That(() => a * b == 10, () => a);
      A.That(() => a * b == 10, b);
      A.That(() => a * b == 10, "test", () => new { a, b });
      
      Assert(() => a * b == 10);
      Assert(() => a * b == 10, () => a);
      Assert(() => a * b == 10, b);
      Assert(() => a * b == 10, "test", () => new { a, b });
    }

    [Fact]
    public void AssertThat_works_if_assertion_is_false()
    {
      var a = 10;
      var b = 1;
      
      Xunit.Assert.Throws<XunitException>(() => A.That(() => a < b));
      Xunit.Assert.Throws<XunitException>(() => A.That(() =>a < b, () => b));
      Xunit.Assert.Throws<XunitException>(() => A.That(() =>a < b, a));
      Xunit.Assert.Throws<XunitException>(() => A.That(() =>a < b, "test", () => new { a, b}));
      Xunit.Assert.Throws<XunitException>(() => Assert(() => a < b));
      Xunit.Assert.Throws<XunitException>(() => Assert(() =>a < b, () => b));
      Xunit.Assert.Throws<XunitException>(() => Assert(() =>a < b, a));
      Xunit.Assert.Throws<XunitException>(() => Assert(() =>a < b, "test", () => new { a, b}));
    }

    void Explode() => throw new InvalidOperationException();

    void Fizzle()
    {
      
    }

    [Fact]
    public void AssertThrows_works_if_an_exception_is_really_thrown()
    {
      string value = null;
      
      A.Throws(() => value.Length);
      A.Throws(() => value.Length == 10);
      A.Throws<NullReferenceException>(() => value.Length);
      A.Throws<NullReferenceException>(() => value.Length == 10);
      A.Throws<InvalidOperationException>(() => Explode());
      A.Throws(() => Explode());
      Throws(() => value.Length);
      Throws(() => value.Length == 10);
      Throws<NullReferenceException>(() => value.Length);
      Throws<NullReferenceException>(() => value.Length == 10);
      Throws<InvalidOperationException>(() => Explode());
      Throws(() => Explode());
    }
    
    [Fact]
    public void AssertThrows_throws_if_an_exception_is_not_thrown()
    {
      string value = "bla";
      
      Xunit.Assert.Throws<XunitException>(() => A.Throws(() => value.Length));
      Xunit.Assert.Throws<XunitException>(() => A.Throws(() => value.Length == 10));
      Xunit.Assert.Throws<XunitException>(() => A.Throws<NullReferenceException>(() => value.Length));
      Xunit.Assert.Throws<XunitException>(() => A.Throws<NullReferenceException>(() => value.Length == 10));
      Xunit.Assert.Throws<XunitException>(() => A.Throws<InvalidOperationException>(() => Fizzle()));
      Xunit.Assert.Throws<XunitException>(() => A.Throws(() => Fizzle()));
      Xunit.Assert.Throws<XunitException>(() => Throws(() => value.Length));
      Xunit.Assert.Throws<XunitException>(() => Throws(() => value.Length == 10));
      Xunit.Assert.Throws<XunitException>(() => Throws<NullReferenceException>(() => value.Length));
      Xunit.Assert.Throws<XunitException>(() => Throws<NullReferenceException>(() => value.Length == 10));
      Xunit.Assert.Throws<XunitException>(() => Throws<InvalidOperationException>(() => Fizzle()));
      Xunit.Assert.Throws<XunitException>(() => Throws(() => Fizzle()));
    }

    [Fact]
    public void Only_two_types_are_exposed_publically()
    {
      var publicTypes = typeof(Assert).Assembly.GetTypes().Where(t => t.IsPublic);

      Assert(() => publicTypes.Count() == 2 && publicTypes.All(t => t.IsAbstract && t.IsSealed));
    }
  }
}