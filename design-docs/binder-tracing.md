# Assembly binder tracing improvements

## Problem definition
In .NET Core assembly binding and resolution is a relatively complicated process with some non-obvious behaviors. It also acts as a black-box, where if something goes wrong, the only indication of what went wrong is the exception with which it fails. While we're going to improve the exception messages to include more information, for more complex scenarios, good exception message is not going to provide enough information to solve all  issues.  
There are cases where the binding succeeds, but the outcome is not what was intended (and the program may fail later on due to the wrong assembly being loaded). In these cases there are no exception and thus we need a different mechanism for diagnostics.  
Last but not least the assembly resolution algorithm is extensible through either custom `AssemblyLoadContext` (ALC) or through various events. Debugging these is not easy since they're called by the runtime without much context as to why they are being called and what happened in the binding so far.

## High-level solution
Add improved tracing/logging functionality in the runtime assembly binding area. The produced information should be directly usable to solve assembly binding issues.  
This will inevitably be compared to fusion logs from .NET Framework. This improvement is not intended as a .NET Core implementation of fusion logs. That said it's trying to solve very similar problems so some aspects of the solution will be similar.

## Existing tracing
There already is some logging/tracing in the runtime around some aspects of the binding process. These will have to be investigated in detail and potentially reused. Open questions
* Are these usable (do they work in .NET Core at all) and if so what is the usage scenario?
* Do they provide the level of detail we would like?
* Can they be kept as is, or can they be repurposed?
* Is any of these mechanism close to the desired solution and thus it would be better to extend it?

### Fusion log
One of the logging mechanisms which remained in the codebase from the .NET Framework is the fusion logging. In the code this is behind the `FEATURE_VERSIONING_LOG` define. It can be activated even on .NET Core by setting `COMPLUS_ForceLog=1` and `COMPLUS_LogPath=<path>`. Currently this is only enabled on Windows (not sure why).
The logging is implemented in the [`BindingLog`](https://github.com/dotnet/coreclr/blob/master/src/binder/bindinglog.cpp) class.  
The logging is done by caching messages in a variable attached to the `ApplicationContext`. The logging is currently scoped to only `AssemblyBinder::BindAssembly` and its callgraph.  
* Tightly coupled with the binder, relies on passing around an instance (`ApplicationContext` in this case) to all the methods which need it. Would be expensive and tricky to extend to other layers.
* Only suitable for native code, exposing it to managed code would be relatively problematic.
* Only supports writing to a file.
* Currently very incomplete since it doesn't write lot of the interesting bits as the `AssemblyBinder` is basically the lowest level of the binding algorithm and there are lot of decisions made in the higher level components.

### Binder log
The binder also uses another logging which is much more detailed than the Fusion logging. It uses macros like `BINDER_LOG_ENTER, BINDER_LOG_LEAVE, BINDER_LOG_STRING, ...`. This also only writes to a file. It is very detailed and includes lot of implementation details. Also seems to currently not be enabled in the build at all.
This logging has larger scope than the above function log and covers more components, but it's still only in the lower levels of the binder. The higher level binder code doesn't have this.
* Too much detail - could be trimmed, but then it loses the existing value.
* Native only - calling this from managed code could be problematic.
* No context - there's no context describing the binding event which caused the logging, all logging is intertwined.
* Could be relatively easily extended to higher levels of the binder (except managed code).

## Goals for the tracing
This section contains samples of traces. Don't look for a spec for the specific traces in here, these are only to illustrate the more general concepts.

### Ability to correlate traces
All traces about a single assembly bind event should be easy to correlate. To that end there will be start/end traces like this:
```
Assembly bind start - //1/2
    assembly: "TestAssembly, version=1.2.3.0",
    requestor: AsmRef from "AppAssembly, version=1.0.0.0" location /path/AppAssembly.dll
```
```
Assembly bind end - //1/2
    Success
    Bound to assembly "TestAssembly, version=1.2.3.0" location /path/TestAssembly.dll
```

All traces about the binding will be in-between these start/end traces and will include the `//1/2` identifier. Other than correlating binder events themselves there are opportunities to use this with sub-components of the binder to annotate their own events/traces. For example the future `.deps.json` based `AssemblyLoadContext` implementation will call into the native CLR host components. It would pass this activity ID so that these components can include it in their own tracing and thus it can be correlated to the binder tracing. Currently we're using the EventSource activity ID typical representation, but the exact format will be determined later on.

### Extension points
The binder algorithm in the runtime calls several different potentially user-defined extension points on both `AssemblyLoadContext` and `AppDomain`. Each such extension point invocation should be wrapped in begin/end traces. For example:
```
Calling AssemblyLoadContext.Load - //1/2
    AssemblyLoadContext: App.CustomALC "My app ALC"
    assembly: "TestAssembly, version=1.2.3.0"
```
```
Returned from AssemblyLoadContext.Load - //1/2
    AssemblyLoadContext: App.CustomALC "My app ALC"
    Returned assembly: null
```

Similarly for event handlers. This would let consumers of the tracing reason about the extension points even if the implementation of the extension point itself doesn't participate in binder tracing.  

Additionally the tracing should be usable from the implementation of the extension points. So that for example our own implementation of ALC (the future `.deps.json` based `AssemblyLoadContext`), or our own event handlers (the resolve event handler in `Assembly.LoadFrom`) can trace additional information. An example of such tracing could be (from `Assembly.LoadFrom`):
```
Resolution of assembly in Assembly.LoadFrom resolve handler - //1/2
    assembly name: TestDependency
    requesting assembly: "PluginMain, version=1.0.0.0" location /path/PluginMain.dll
    Requesting assembly was loaded with Assembly.LoadFrom, using LoadFrom resolution for the dependency.
```
```
Assembly.LoadFrom dependency resolution probing - //1/2
    attempting to load assembly from path: /path/TestDependency.dll
```
These traces are specific to the `Assembly.LoadFrom` implementation and are not really part of the binder in any way. But they can still use the same tracing mechanisms and are correlated through the activity ID.

Eventually this tracing should be available to 3rd party extensions as well, with the same benefits as 1st party components.

### Resolution fallback flow
The binder implements a series of fallbacks as it tries to resolve the assembly. The traces should clearly describe what was tried (and potentially why). For example traces like
```
Loading into custom AssemblyLoadContext - //1/2
    AssemblyLoadContext: App.CustomALC "My app ALC"
```
```
Falling back to AssemblyLoadContext.Default - //1/2
```
```
Trying to resolve with AssemblyLoadContext.Resolving event handlers - //1/2
```
```
Trying to resolve with AppDomain.AssemblyResolve event handlers - //1/2
```

### Binding attempts
Terminology: Binding attempt here means the mechanism in the runtime which tries to find/add assembly name to a load context. This operation on its own doesn't actually load/bind the assembly, it just reconciles it with the load context. This operation is relatively frequent in the binder, for one external binding event there can be several binding attempts done internally.
Each binding attempt should exactly describe what we're trying to bind, where we're trying to bind it and what was the outcome. This potentially overlaps with improvements to exception messages in the binder.
Example traces:
```
Binding attempt - //1/2
    assembly: "TestAssembly, version=1.2.3.0"
    AssemblyLoadContext: Default
    result: Failed
    reason: No assembly with name "TestAssembly" is loaded into the loader context
```
```
Binding attempt - //1/2
    assembly: "TestAssembly, version=2.4.0.0"
    AssemblyLoadContext: Default
    result: Failed
    reason: Loader context already contains assembly "TestAssembly" but with lower version "1.2.3.0"
```
```
Binding attempt - //1/2
    assembly: "TestAssembly, version=1.2.3.0"
    AssemblyLoadContext: Default
    result: Success
    bound assembly: "TestAssembly, version=1.4.0.0"
```

### Nesting
During assembly resolution it's very common to recursively call into methods which act as binder entry points. For example `Assembly.LoadFrom`, `Assembly.Load` or `AssemblyLoadContext.LoadFromAssemblyPath`. These methods are used to implement the actual resolution logic in custom ALC or event handlers.
It's unclear if we would treat these as truly nested binding attempts, or instead if we would somehow fold them into the active binding operation. In any case we would have to keep the //1/2 in some form.
It seems that we should include begin/end traces for these operations regardless. An example of "folded" traces:
```
Begin AssemblyLoadContext.LoadFromAssemblyPath - //1/2
    AssemblyLoadContext: App.CustomALC "My app ALC"
    assembly path: /path/TestAssembly.dll
```
```
End AssemblyLoadContext.LoadFromAssemblyPath - //1/2
    AssemblyLoadContext: App.CustomALC "My app ALC"
    Loaded assembly: "TestAssembly, version=1.2.3.0" location /path/TestAssembly.dll
```

## Sample diagnostic scenarios
This section tries to describe common scenarios where binder diagnostics would help solve issues. It also tries to list the various pieces of information which the tracing should include to be helpful in the respective scenario.

### Wrong version of assembly loaded
If the assembly binding was successful but the wrong file was loaded there's no apparent place to store information about the binding results (like exceptions are for the failure cases). The tracing in the simple case would provide
* What exact assembly was loaded (path, version, ...)
* Who asked for the load (for example the assembly which had the assembly reference)
* Where in the binder process we decided to look at the file which was loaded

### Unexpected extension point inclusion
The application implements some of the binder extension points, but the user is not aware of this, or is not expecting these extension points to be involved in the specific bind event. The tracing should include
* Which extension points were called
* Which one actually resolved the bind event
* At least this information for all extension points regardless if they are aware of the tracing or not
* Potentially more details from extension points which are aware of and use the tracing

### Development of binder extension point
The user is trying to implement an extension point for the binder (event handler, or custom ALC). Debugging the code without additional information is relatively hard since the code flows between the user code, low level framework and the runtime a lot. Call stacks can be confusing and order of execution is complicated. The tracing should help by providing:
* The overall description of the bind event (what is trying to bind to what and so on)
* Why the extension point was called and what has been tried before
* What is the result of the extension point as seen by the binder
* False positives - either unexpected successful bind or bind to the wrong thing outside of the extension point

## Tracing mechanisms
This section describes the proposed mechanisms to be used.

### Tracing mechanism
The core tracing mechanism will be `EventSource` which allows the tracing to use ETW, EventPipe or LTTng and is extensible. The tracing API will implement its own event source and probably define several specific events (like start/end of bind and so on) as well as a generic event for the rest. This mechanism will fulfill scenarios like Azure infrastructure and other production deployment scenarios where OS based tracing mechanism is preferred (especially ETW on Windows).  

Depending on improvements in the general tracing infrastructure there might be a need for a secondary route which would write the tracing into a file (probably triggered by an environment variable). Currently it seems that in order to provide the best value with this feature we need a very easy way to consume the traces which works everywhere. Humanly readable text files are a great fit for this.

### Tracing API
The binder code is both in native and managed. The tracing will be invoked form both. Given that it's MUCH easier to implement custom `EventSource` in managed code we will do so and for the native callers have a small wrapper which will call the managed code from native.

We don't plan to expose this event source to 3rd party components. Those should define their own even source and correlate with 1ts party binder tracing through activity IDs.

### Context
Event source provides a mechanism to correlate traces which belong to one operation through activity IDs. The entry points to the binder would start a new activity and wrap all of the bind into it. That way all events (not just those from the binder, but any event from that thread) will get that activity ID and thus should be easy to correlate to each other. The event source infrastructure maintains the activity ID per thread and in cooperation with TPL it can even correctly trace it across async tasks. See this [blog post](https://blogs.msdn.microsoft.com/vancem/2015/09/14/exploring-eventsource-activity-correlation-and-causation-features/) for more details.

### Anticipated complexities
* Binder doesn't have a clear single entry point. Instead there are several different methods and mechanisms which can lead into the binder (`Assembly.Load*`, `AssemblyLoadContext`, implicit resolution of assembly references, ...). As such it's relatively tricky to create a consistent Start/Stop traces around each binder operation.
* Binder is internally recursive. In lot of cases binder operation leads into another nested binder operation. `EventSource` activity tracking by default doesn't handle recursion (intentional). We could enable it, but then we would have to guarantee correct Start/Stop matching on our end in all cases (even on error conditions). It will probably be simpler to have a thread local variable which marks the thread as running binder operation and only Start a new binder operation if that's not the case. We would still try to correctly call Start/Stop but it would no longer be absolutely necessary to do so in all cases.
* Binder and `EventSource` are recursive - that is it can happen that a call to event source will end up invoking binder. This could lead to endless recursion. Event source itself is implemented in `System.Private.CoreLib` so it will not cause new binds, but potential extensions like event listeners will not and thus may cause bind operations. This problem already exists in event source today and as of now we push the responsibility handle this correctly to the listeners. As this is somewhat orthogonal to binder tracing (binder is just yet another source of such potential recursion) we're not trying to solve it here.

## What's still missing
TODOs for this document:
* Native dependency binder - should the same mechanism be used for native dependency resolution (PInvoke)? If so:
    * DllMap interactions?
    * Is there interaction between managed resolution and native resolution?
    * Describe scenarios
* What are the internal runtime entry points to the binder (outside of `Assembly.Load*` and `AssemblyLoadContext`)? Can we correctly Start/Stop binder operations in these cases?
