using CloudShell.Abstractions.ResourceManager;
using CloudShell.Providers.Applications;

namespace CloudShell.Abstractions.Tests;

public sealed class ApplicationRuntimeStateTrackerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 23, 15, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(ResourceState.Starting)]
    [InlineData(ResourceState.Stopping)]
    public void GetState_PreservesFreshTransientState(ResourceState state)
    {
        var store = new FakeRuntimeStateStore();
        store.Save(new ApplicationRuntimeState(
            "application:api",
            null,
            null,
            Now.AddMinutes(-1),
            State: state));
        var tracker = CreateTracker(store, _ => false);

        Assert.Equal(state, tracker.GetState("application:api"));
    }

    [Theory]
    [InlineData(true, ResourceState.Running)]
    [InlineData(false, ResourceState.Stopped)]
    public void GetState_FallsBackToObservedRuntimeWhenTransientStateExpires(
        bool running,
        ResourceState expected)
    {
        var store = new FakeRuntimeStateStore();
        store.Save(new ApplicationRuntimeState(
            "application:api",
            null,
            null,
            Now.AddMinutes(-10),
            State: ResourceState.Starting));
        var tracker = CreateTracker(store, _ => running);

        Assert.Equal(expected, tracker.GetState("application:api"));
    }

    [Fact]
    public void GetState_FallsBackToObservedRuntimeWithoutStoredState()
    {
        var tracker = CreateTracker(new FakeRuntimeStateStore(), _ => true);

        Assert.Equal(ResourceState.Running, tracker.GetState("application:api"));
    }

    [Fact]
    public void MarkStarting_CreatesOrUpdatesTransientState()
    {
        var store = new FakeRuntimeStateStore();
        store.Save(new ApplicationRuntimeState(
            "application:api",
            LastKnownProcessId: 42,
            LastKnownProcessStartedAt: Now.AddHours(-1),
            LastObservedAt: Now.AddHours(-1),
            State: ResourceState.Running));
        var tracker = CreateTracker(store, _ => false);

        tracker.MarkStarting("application:api");

        var state = store.Get("application:api");
        Assert.NotNull(state);
        Assert.Equal(ResourceState.Starting, state.State);
        Assert.Equal(Now, state.LastObservedAt);
        Assert.Equal(42, state.LastKnownProcessId);
    }

    [Fact]
    public void ClearStarting_StopsOnlyWhenStillStartingAndNotRunning()
    {
        var store = new FakeRuntimeStateStore();
        store.Save(new ApplicationRuntimeState(
            "application:api",
            null,
            null,
            Now.AddMinutes(-1),
            State: ResourceState.Starting));
        var tracker = CreateTracker(store, _ => false);

        tracker.ClearStarting("application:api");

        var state = store.Get("application:api");
        Assert.NotNull(state);
        Assert.Equal(ResourceState.Stopped, state.State);
        Assert.Equal(Now, state.LastObservedAt);
    }

    [Fact]
    public void ClearStarting_DoesNotOverwriteRunningObservation()
    {
        var store = new FakeRuntimeStateStore();
        store.Save(new ApplicationRuntimeState(
            "application:api",
            null,
            null,
            Now.AddMinutes(-1),
            State: ResourceState.Starting));
        var tracker = CreateTracker(store, _ => true);

        tracker.ClearStarting("application:api");

        Assert.Equal(ResourceState.Starting, store.Get("application:api")?.State);
    }

    [Fact]
    public void MarkStoppingAndClearStopping_ProjectStoppedTransition()
    {
        var store = new FakeRuntimeStateStore();
        var tracker = CreateTracker(store, _ => false);

        tracker.MarkStopping("application:api");
        Assert.Equal(ResourceState.Stopping, store.Get("application:api")?.State);

        tracker.ClearStopping("application:api");
        Assert.Equal(ResourceState.Stopped, store.Get("application:api")?.State);
    }

    private static ApplicationRuntimeStateTracker CreateTracker(
        IApplicationRuntimeStateStore store,
        Func<string, bool> isRunning) =>
        new(
            store,
            isRunning,
            () => Now,
            TimeSpan.FromMinutes(5));

    private sealed class FakeRuntimeStateStore : IApplicationRuntimeStateStore
    {
        private readonly Dictionary<string, ApplicationRuntimeState> states = new(StringComparer.OrdinalIgnoreCase);

        public ApplicationRuntimeState? Get(string applicationId) =>
            states.GetValueOrDefault(applicationId);

        public void Save(ApplicationRuntimeState state) =>
            states[state.ApplicationId] = state;
    }
}
