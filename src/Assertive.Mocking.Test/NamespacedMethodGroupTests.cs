using Assertive.Mocking;
using Xunit;
using static Assertive.DSL;
using static Assertive.Mocking.Mock;

namespace Assertive.Mocking.Test.Namespaced
{
  public interface INamespacedRepository
  {
    int GetById(int id);
  }

  public class NamespacedMethodGroupTests
  {
    [Fact]
    public void Method_group_Any_on_namespaced_interface_resolves()
    {
      var repo = A<INamespacedRepository>();

      Any(repo.GetById).Returns(5);

      Assert(() => repo.GetById(3) == 5);
    }
  }
}
