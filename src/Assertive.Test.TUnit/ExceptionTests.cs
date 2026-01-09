using TUnit.Assertions;
using TUnit.Assertions.Exceptions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace Assertive.Test.TUnit;

public class ExceptionTests
{
  [Test]
  public async Task Assert_that_throws_correct_exception_type()
  {
    var threw = false;

    try
    {
      Assertive.Assert.That(() => false);
    }
    catch (AssertionException)
    {
      threw = true;
    }

    await global::TUnit.Assertions.Assert.That(() => threw).IsTrue();
  }

  [Test]
  public async Task DSL_throws_correct_exception_type()
  {
    var threw = false;

    try
    {
      DSL.Assert(() => false);
    }
    catch (AssertionException)
    {
      threw = true;
    }

    await global::TUnit.Assertions.Assert.That(() => threw).IsTrue();
  }
}
