using System.CommandLine;
using LocalSendDotNet;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tonarink.Cli;

namespace Tonarink.CommandLine.Tests;

[TestClass]
[TestCategory("Unit")]
public sealed class CommandBehaviorTests
{
    [TestMethod]
    [DataRow("start", 0)]
    [DataRow("stop", 1)]
    [DataRow("restart", 2)]
    public async Task ServerCommand_InvokesCorrespondingRuntimeAction(string command, int action)
    {
        var runtime = new RuntimeSpy();
        var responses = new List<CliResponse>();
        Assert.AreEqual(0, await Invoke(runtime, ["server", command], responses));
        Assert.AreEqual((CliServerAction)action, runtime.ServerAction);
        Assert.ContainsSingle(responses);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SettingsGet_PropagatesExplicitSecretPermission(bool showSecrets)
    {
        var runtime = new RuntimeSpy();
        var responses = new List<CliResponse>();
        string[] arguments = ["settings", "get", "ALIAS", .. showSecrets ? new[] { "--show-secrets" } : Array.Empty<string>()];
        Assert.AreEqual(0, await Invoke(runtime, arguments, responses));
        Assert.AreEqual(showSecrets, runtime.ShowSecrets);
        var response = Assert.ContainsSingle(responses);
        Assert.IsNotNull(response.Data);
        Assert.AreEqual("alias", response.Data.Value[0].GetProperty("key").GetString());
        Assert.AreEqual("test device", response.Data.Value[0].GetProperty("value").GetString());
    }

    [TestMethod]
    public async Task UnknownSetting_EmitsBusinessErrorNotAnEmptySuccessfulResult()
    {
        var responses = new List<CliResponse>();
        Assert.AreEqual(2, await Invoke(new RuntimeSpy(), ["settings", "get", "unknown"], responses));
        Assert.AreEqual(2, Assert.ContainsSingle(responses).ExitCode);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SettingsSet_PreservesCallerDirectoryValueAndRestartFlag(bool restart)
    {
        var runtime = new RuntimeSpy();
        var responses = new List<CliResponse>();
        string[] arguments = ["settings", "set", "alias", "测试 device", .. restart ? new[] { "--restart" } : Array.Empty<string>()];
        Assert.AreEqual(0, await Invoke(runtime, arguments, responses));
        Assert.AreEqual(("alias", "测试 device", Path.GetTempPath(), restart), runtime.SavedSetting);
    }

    [TestMethod]
    [DataRow("business", 9)]
    [DataRow("cancelled", 130)]
    [DataRow("io", 1)]
    public async Task RuntimeFailure_EmitsMatchingFinalResponseAndExitCode(string kind, int expected)
    {
        var runtime = new RuntimeSpy
        {
            SaveFailure = kind switch
            {
                "business" => new CliException("specific business failure", 9),
                "cancelled" => new OperationCanceledException(),
                _ => new IOException("synthetic persistence failure"),
            },
        };
        var responses = new List<CliResponse>();
        Assert.AreEqual(expected, await Invoke(runtime, ["settings", "set", "alias", "test"], responses));
        var response = Assert.ContainsSingle(responses);
        Assert.AreEqual(expected, response.ExitCode);
        Assert.AreEqual("result", response.Type);
        Assert.IsTrue(response.Message?.Length > 0);
    }

    [TestMethod]
    public async Task HistoryClear_RequestsRemovalOfAllRecords()
    {
        var runtime = new RuntimeSpy();
        Assert.AreEqual(0, await Invoke(runtime, ["history", "clear", "--yes"], []));
        Assert.IsTrue(runtime.HistoryRemoved);
        Assert.IsNull(runtime.RemovedHistoryId);
    }

    [TestMethod]
    public async Task HistoryRemove_PassesExactRecordIdentity()
    {
        var runtime = new RuntimeSpy();
        var id = Guid.NewGuid();
        Assert.AreEqual(0, await Invoke(runtime, ["history", "remove", id.ToString()], []));
        Assert.IsTrue(runtime.HistoryRemoved);
        Assert.AreEqual(id, runtime.RemovedHistoryId);
    }

    [TestMethod]
    [DataRow("favorite")]
    [DataRow("history")]
    [DataRow("main")]
    public async Task IntegratedOpen_PassesSelectedDestination(string destination)
    {
        var runtime = new RuntimeSpy();
        var id = Guid.NewGuid();
        string[] arguments = ["app", "open", .. destination switch
        {
            "favorite" => new[] { "--favorite", "saved-fingerprint" },
            "history" => new[] { "--history", id.ToString() },
            _ => Array.Empty<string>(),
        }];
        Assert.AreEqual(0, await Invoke(runtime, arguments, [], integrated: true));
        Assert.IsTrue(runtime.Opened);
        Assert.AreEqual(destination == "favorite" ? "saved-fingerprint" : null, runtime.OpenFavorite);
        Assert.AreEqual(destination == "history" ? (Guid?)id : null, runtime.OpenHistory);
    }

    [TestMethod]
    public async Task Status_ReadsHostStateWithoutStartingServer()
    {
        var runtime = new RuntimeSpy();
        var responses = new List<CliResponse>();
        Assert.AreEqual(0, await Invoke(runtime, ["status"], responses));
        Assert.IsNull(runtime.ServerAction);
        var response = Assert.ContainsSingle(responses);
        Assert.IsNotNull(response.Data);
        Assert.AreEqual("Stopped", response.Data.Value.GetProperty("state").GetString());
        Assert.IsTrue(response.Data.Value.GetProperty("appRunning").GetBoolean());
    }

    [TestMethod]
    [DataRow("text")]
    [DataRow("empty")]
    [DataRow("image-preview")]
    [DataRow("no-preview")]
    [DataRow("multiple")]
    public async Task IncomingText_OnlySingleTextPreviewIsExposed(string kind)
    {
        var preview = kind switch { "empty" => "", "no-preview" => null, _ => "  文本 🌏\n " };
        var first = new IncomingItem("first", "message.txt", 0,
            kind == "image-preview" ? "image/png" : "TEXT/PLAIN", null, preview, null, null);
        IncomingItem[] items = kind == "multiple" ? [first, first with { Id = "second", FileName = "second.txt" }] : [first];
        var peer = new LocalSendDevice("Preview sender", "2.2", null, LocalSendDeviceType.Headless,
            new string('A', 64), false, [], DateTimeOffset.UtcNow);
        var incoming = new IncomingTransferRequest(Guid.NewGuid(), Guid.NewGuid(), "session", peer, items, DateTimeOffset.UtcNow);
        var runtime = new RuntimeSpy { Incoming = [incoming] };
        var responses = new List<CliResponse>();
        Assert.AreEqual(0, await Invoke(runtime, ["receive", "list"], responses));
        var response = Assert.ContainsSingle(responses);
        Assert.IsNotNull(response.Data);
        var request = response.Data.Value[0];
        Assert.AreEqual(incoming.RequestId, request.GetProperty("id").GetGuid());
        Assert.HasCount(items.Length, request.GetProperty("files").EnumerateArray().ToArray());
        if (kind is "text" or "empty") Assert.AreEqual(preview, request.GetProperty("text").GetString());
        else Assert.IsFalse(request.TryGetProperty("text", out _));
        Assert.IsNull(runtime.ServerAction);
    }

    private static async Task<int> Invoke(RuntimeSpy runtime, string[] arguments, List<CliResponse> responses, bool integrated = false)
    {
        using var language = CliText.UseLanguage("en-US");
        var cli = new CliCommandLine(integrated, runtime: runtime,
            request: new(arguments, Path.GetTempPath(), null),
            emit: (response, _) => { responses.Add(response); return ValueTask.CompletedTask; }, reportProgress: responses.Add);
        var parse = cli.Root.Parse(arguments);
        Assert.IsEmpty(parse.Errors);
        return await parse.InvokeAsync();
    }

    // A platform-boundary spy only. Production System.CommandLine and
    // CliCommands own parsing, validation, error handling and JSON responses.
    private sealed class RuntimeSpy : ICliRuntime
    {
        public bool Integrated => false;
        public CliServerAction? ServerAction { get; private set; }
        public bool ShowSecrets { get; private set; }
        public (string Key, string Value, string Directory, bool Restart)? SavedSetting { get; private set; }
        public Exception? SaveFailure { get; init; }
        public bool HistoryRemoved { get; private set; }
        public Guid? RemovedHistoryId { get; private set; }
        public bool Opened { get; private set; }
        public string? OpenFavorite { get; private set; }
        public Guid? OpenHistory { get; private set; }
        public IncomingTransferRequest[] Incoming { get; init; } = [];
        public Task<CliState> ReadAsync(CancellationToken token) => Task.FromResult(new CliState(null, false,
            LocalSendNodeState.Stopped, null, null, null, Incoming, new(Path.GetTempPath(), true, true, true, "en-US")));
        public Task<CliResponse> ServerAsync(CliServerAction action, CancellationToken token)
        {
            ServerAction = action;
            return Task.FromResult(CliProtocol.Success("server action completed"));
        }
        public Task<CliSetting[]> ReadSettingsAsync(bool showSecrets, CancellationToken token)
        {
            ShowSecrets = showSecrets;
            return Task.FromResult<CliSetting[]>([new("alias", "test device")]);
        }
        public Task<CliResponse> SaveSettingAsync(string key, string value, string workingDirectory, bool restart, CancellationToken token)
        {
            if (SaveFailure is not null)
                return Task.FromException<CliResponse>(SaveFailure);
            SavedSetting = (key, value, workingDirectory, restart);
            return Task.FromResult(CliProtocol.Success("setting saved"));
        }
        public Task RemoveHistoryAsync(Guid? id, CancellationToken token)
        {
            HistoryRemoved = true;
            RemovedHistoryId = id;
            return Task.CompletedTask;
        }
        public Task OpenAppAsync(string? favorite, Guid? history, CancellationToken token)
        {
            Opened = true;
            OpenFavorite = favorite;
            OpenHistory = history;
            return Task.CompletedTask;
        }
        public Task<CliFavorite[]> FavoritesAsync(CancellationToken token) => throw new NotSupportedException();
        public Task SaveFavoriteAsync(LocalSendDevice device, CancellationToken token) => throw new NotSupportedException();
        public Task RemoveFavoriteAsync(string fingerprint, CancellationToken token) => throw new NotSupportedException();
        public Task<CliHistory[]> HistoryAsync(CancellationToken token) => throw new NotSupportedException();
        public Task RecordReceiveAsync(IncomingTransferRequest request, ReceiveOutcome.Completed result, CancellationToken token) => throw new NotSupportedException();
        public Task DismissIncomingAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        public IDisposable BeginReceiveWatch() => throw new NotSupportedException();
        public Task QuitAsync(CancellationToken token) => throw new NotSupportedException();
    }
}
