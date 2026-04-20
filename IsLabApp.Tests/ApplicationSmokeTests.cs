namespace IsLabApp.Tests;

public class ApplicationSmokeTests
{
    [Fact]
    public void ApplicationAssembly_HasExpectedName()
    {
        var assemblyName = typeof(Program).Assembly.GetName().Name;

        Assert.Equal("IsLabApp", assemblyName);
    }
}
