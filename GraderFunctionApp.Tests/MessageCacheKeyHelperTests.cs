using GraderFunctionApp.Helpers;

namespace GraderFunctionApp.Tests;

public class MessageCacheKeyHelperTests
{
    [Test]
    public void CreateNpcKey_UsesStableSha256Components()
    {
        var result = MessageCacheKeyHelper.CreateNpcKey(
            "Hello",
            30,
            "female",
            "teacher");

        Assert.That(
            result,
            Is.EqualTo(
                "npc_30_" +
                "nxZROajCiUpHrqI7d9Mw7KhHJkIkpE1aF7GduLmnLAg_" +
                "EFepYE4EsnTaWk3gyPS0ho2bIwmJ-MjGooIhFDzFp1U_" +
                "GF-NsyJx_iX1Yab8k4suJkMG7DBO2lGAB9F2SCY4GWk"));
    }
}
