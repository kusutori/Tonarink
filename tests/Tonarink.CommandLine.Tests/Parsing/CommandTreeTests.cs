using System.CommandLine;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tonarink.Cli;

namespace Tonarink.CommandLine.Tests;

[TestClass]
[TestCategory("Unit")]
public sealed class CommandTreeTests
{
    // Each row names a public command, not a reimplementation of its syntax.
    // Invoke the production parser/action with a forwarding spy; runtime behavior
    // is exercised separately with ICliRuntime and real process fixtures.
    [TestMethod]
    [DataRow("status")]
    [DataRow("devices")]
    [DataRow("discover")]
    [DataRow("send", "--target", "receiver", "--text", "hello")]
    [DataRow("cancel", "b1085dc2-255c-4ed8-abd1-c9f0cc4895e0")]
    [DataRow("receive", "list")]
    [DataRow("receive", "accept", "b1085dc2-255c-4ed8-abd1-c9f0cc4895e0")]
    [DataRow("receive", "decline", "b1085dc2-255c-4ed8-abd1-c9f0cc4895e0")]
    [DataRow("receive", "watch", "--seconds", "1")]
    [DataRow("server", "start")]
    [DataRow("server", "stop")]
    [DataRow("server", "restart")]
    [DataRow("settings", "list")]
    [DataRow("settings", "get", "alias")]
    [DataRow("settings", "set", "alias", "测试 device")]
    [DataRow("favorites", "list")]
    [DataRow("favorites", "add", "127.0.0.1")]
    [DataRow("favorites", "remove", "fingerprint")]
    [DataRow("history", "list")]
    [DataRow("history", "remove", "b1085dc2-255c-4ed8-abd1-c9f0cc4895e0")]
    [DataRow("history", "clear", "--yes")]
    [DataRow("web", "send", "--text", "hello")]
    [DataRow("web", "receive")]
    [DataRow("web", "status")]
    [DataRow("web", "stop")]
    [DataRow("web", "accept", "session")]
    [DataRow("web", "decline", "session")]
    [DataRow("app", "quit", "--yes")]
    public async Task BusinessLeaf_ValidArguments_ForwardsExactlyOnce(params string[] arguments)
    {
        var calls = 0;
        var cli = new CliCommandLine(integrated: false, forward: (parsed, _) =>
        {
            calls++;
            Assert.AreSequenceEqual(arguments, parsed.Tokens.Select(t => t.Value));
            return Task.FromResult(17);
        });
        var parse = cli.Root.Parse(arguments);
        Assert.IsEmpty(parse.Errors);
        Assert.IsTrue(cli.IsBusinessInvocation(parse));
        Assert.AreEqual(17, await parse.InvokeAsync());
        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    [DataRow("send", "--text", "hello")]
    [DataRow("send", "--target", "receiver")]
    [DataRow("send", "--target", "receiver", "file.txt", "--text", "hello")]
    [DataRow("cancel", "invalid-id")]
    [DataRow("receive", "accept", "invalid-id")]
    [DataRow("history", "remove", "invalid-id")]
    [DataRow("history", "clear")]
    [DataRow("app", "quit")]
    [DataRow("settings", "set", "alias")]
    [DataRow("status", "--unknown")]
    [DataRow("status", "--language", "invalid")]
    [DataRow("status", "--timeout", "0")]
    [DataRow("status", "--timeout", "86401")]
    [DataRow("discover", "--seconds", "abc")]
    public async Task InvalidArguments_ReturnParseErrorWithoutForwarding(params string[] arguments)
    {
        var calls = 0;
        var cli = new CliCommandLine(false, forward: (_, _) => { calls++; return Task.FromResult(0); });
        var parse = cli.Root.Parse(arguments);
        Assert.IsNotEmpty(parse.Errors);
        using var output = new StringWriter();
        using var error = new StringWriter();
        // System.CommandLine's parse-error action returns 1. CliClient maps the
        // external process's syntax errors to 2; assert that in process tests.
        Assert.AreEqual(1, await parse.InvokeAsync(new InvocationConfiguration { Output = output, Error = error }));
        Assert.AreEqual(0, calls);
        Assert.IsTrue(error.ToString().Length > 0);
    }

    [TestMethod]
    [DataRow("--help")]
    [DataRow("--version")]
    [DataRow("server", "--help")]
    [DataRow("send", "--help")]
    public async Task HelpAndVersion_DoNotDispatchBusinessAction(params string[] arguments)
    {
        var calls = 0;
        var cli = new CliCommandLine(false, forward: (_, _) => { calls++; return Task.FromResult(0); });
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.AreEqual(0, await cli.Root.Parse(arguments).InvokeAsync(new InvocationConfiguration { Output = output, Error = error }));
        Assert.AreEqual(0, calls);
        Assert.IsTrue(output.ToString().Length > 0);
        Assert.AreEqual(string.Empty, error.ToString());
    }

    [TestMethod]
    public void IntegratedAndStandalone_ExposeOnlyTheirOwnOptions()
    {
        var standalone = new CliCommandLine(false);
        var integrated = new CliCommandLine(true);
        Assert.IsEmpty(standalone.Root.Parse(["status", "--profile", "isolated"]).Errors);
        Assert.IsNotEmpty(integrated.Root.Parse(["status", "--profile", "isolated"]).Errors);
        Assert.IsEmpty(integrated.Root.Parse(["app", "open"]).Errors);
        Assert.IsNotEmpty(standalone.Root.Parse(["app", "open"]).Errors);
        Assert.IsNotEmpty(integrated.Root.Parse(["app", "open", "--favorite", "fp", "--history", Guid.NewGuid().ToString()]).Errors);
    }

    [TestMethod]
    public void InputAndOfflineClassification_UsesActualSelectedCommand()
    {
        var cli = new CliCommandLine(false);
        Assert.IsTrue(cli.ReadsStandardInput(cli.Root.Parse(["send", "--target", "peer", "--text", "-"])));
        Assert.IsFalse(cli.ReadsStandardInput(cli.Root.Parse(["send", "--target", "peer", "--text", "literal"])));
        Assert.IsTrue(cli.CanRunWithoutHost(cli.Root.Parse(["status"])));
        Assert.IsTrue(cli.CanRunWithoutHost(cli.Root.Parse(["server", "stop"])));
        Assert.IsTrue(cli.CanRunWithoutHost(cli.Root.Parse(["app", "quit", "--yes"])));
        Assert.IsFalse(cli.CanRunWithoutHost(cli.Root.Parse(["server", "start"])));
        Assert.IsFalse(cli.IsQuit(cli.Root.Parse(["app", "quit", "--help"])));
    }
}
