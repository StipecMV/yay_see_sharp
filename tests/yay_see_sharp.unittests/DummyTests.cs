using System.Threading.Tasks;

public class DummyTests
{
    [Test]
    public async Task Addition_returns_expected_result()
    {
        var result = 2 + 2;

        await Assert.That(result).IsEqualTo(4);
    }
}
