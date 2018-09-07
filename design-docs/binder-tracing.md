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
* Currently very incomplete wince it doesn't write lot of interesting bits as the `AssemblyBinder` is basically the lowest level of the binding algorithm and there are lot of decisions made in the higher level components.

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
Assembly bind start - assembly bind 12044
    assembly: "TestAssembly, version=1.2.3.0",
    requestor: AsmRef from "AppAssembly, version=1.0.0.0" location /path/AppAssembly.dll
```
```
Assembly bind end - assembly bind 12044
    Success
    Bound to assembly "TestAssembly, version=1.2.3.0" location /path/TestAssembly.dll
```

All traces about the binding will be in-between these start/end traces and will include the `assembly bind 12044` identifier. This bind ID should be opaque and probably with no additional meaning. There are opportunities to use this with sub-components of the binder to annotate their own events/traces. For example the future `.deps.json` based `AssemblyLoadContext` implementation will call into the native CLR host components. It would pass this bind ID so that these components can include in their own tracing and thus it can be correlated to the binder tracing. The value used in this document is just a sample, the exact way to construct the bind ID is yet to be decided.

### Extension points
The binder algorithm in the runtime calls several different potentially user-defined extension points on both `AssemblyLoadContext` and `AppDomain`. Each such extension point invocation should be wrapped in begin/end traces. For example:
```
Calling AssemblyLoadContext.Load - assembly bind 12044
    AssemblyLoadContext: App.CustomALC "My app ALC"
    assembly: "TestAssembly, version=1.2.3.0"
```
```
Returned from AssemblyLoadContext.Load - assembly bind 12044
    AssemblyLoadContext: App.CustomALC "My app ALC"
    Returned assembly: null
```

Similarly for event handlers. This would let consumers of the tracing reason about the extension points even if the implementation of the extension point itself doesn't participate in binder tracing.  

Additionally the tracing should be usable from the implementation of the extension points. So that for example our own implementation of ALC (the future `.deps.json` based `AssemblyLoadContext`), or our own event handlers (the resolve event handler in `Assembly.LoadFrom`) can trace additional information. An example of such tracing could be (from `Assembly.LoadFrom`):
```
Resolution of assembly in Assembly.LoadFrom resolve handler - assembly bind 12044
    assembly name: TestDependency
    requesting assembly: "PluginMain, version=1.0.0.0" location /path/PluginMain.dll
    Requesting assembly was loaded with Assembly.LoadFrom, using LoadFrom resolution for the dependency.
```
```
Assembly.LoadFrom dependency resolution probing - assembly bind 12044
    attempting to load assembly from path: /path/TestDependency.dll
```
These traces are specific to the `Assembly.LoadFrom` implementation and are not really part of the binder in any way. But they can still use the same tracing mechanisms and are correlated through the bind ID.

Eventually this tracing should be available to 3rd party extensions as well, with the same benefits as 1st party components.

### Resolution fallback flow
The binder implements a series of fallbacks as it tries to resolve the assembly. The traces should clearly describe what was tried (and potentially why). For example traces like
```
Loading into custom AssemblyLoadContext - assembly bind 12044
    AssemblyLoadContext: App.CustomALC "My app ALC"
```
```
Falling back to AssemblyLoadContext.Default - assembly bind 12044
```
```
Trying to resolve with AssemblyLoadContext.Resolving event handlers - assembly bind 12044
```
```
Trying to resolve with AppDomain.AssemblyResolve event handlers - assembly bind 12044
```

### Binding attempts
Each binding attempt should exactly describe what we're trying to bind, where we're trying to bind it and what was the outcome. This potentially overlaps with improvements to exception messages in the binder.
Example traces:
```
Binding attempt - assembly bind 12044
    assembly: "TestAssembly, version=1.2.3.0"
    AssemblyLoadContext: Default
    result: Failed
    reason: No assembly with name "TestAssembly" is loaded into the loader context
```
```
Binding attempt - assembly bind 12044
    assembly: "TestAssembly, version=2.4.0.0"
    AssemblyLoadContext: Default
    result: Failed
    reason: Loader context already contains assembly "TestAssembly" but with lower version "1.2.3.0"
```
```
Binding attempt - assembly bind 12044
    assembly: "TestAssembly, version=1.2.3.0"
    AssemblyLoadContext: Default
    result: Success
    bound assembly: "TestAssembly, version=1.4.0.0"
```

### Nesting
During assembly resolution it's very common to recursively call into methods which act as binder entry points. For example `Assembly.LoadFrom`, `Assembly.Load` or `AssemblyLoadContext.LoadFromAssemblyPath`. These methods are used to implement the actual resolution logic in custom ALC or event handlers.
It's unclear if we would treat these as truly nested binding attempts, or instead if we would somehow fold them into the active binding operation. In any case we would have to keep the bind ID in some form.
It seems that we should include begin/end traces for these operations regardless. An example of "folded" traces:
```
Begin AssemblyLoadContext.LoadFromAssemblyPath - assembly bind 12044
    AssemblyLoadContext: App.CustomALC "My app ALC"
    assembly path: /path/TestAssembly.dll
```
```
End AssemblyLoadContext.LoadFromAssemblyPath - assembly bind 12044
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
### Context
In order to provide the correlation of various traces which belong to the same bind event, there has to be a context which is accessible to all components which participate in the tracing. Maybe not at first but eventually we would like the tracing APIs to be available to 3rd party binding extensions (ALCs, event handlers).  
As such we can't rely on passing around instances of some context object. Instead thread local variable would be used to assign the context based on the thread running it.
The context would store the bind ID and potentially other information about the bind event (the original requestor for example).

### Tracing API
Since we want to eventually expose these APIs and also due to implementation choices it seems that it would be better to base the trace APIs in managed code.  
The core tracing APIs would be in managed code (static class with static methods in System.Private.CoreLib). Since we also need to trace from native components, there would be a slim native wrapper which would call into the managed code to perform the actual tracing.

Implementation note: By using managed code there's a potential reentracy issue where trying to trace might cause new bind events. We will have to evaluate the potential solutions to these issues.

### Tracing mechanism
The core tracing mechanism will be `EventSource` which allows the tracing to use ETW, EventPipe or LTTng and is extensible. The tracing API will implement its own event source and probably define several specific events (like start/end of bind and so on) as well as a generic event for the rest. This mechanism will fulfill scenarios like Azure infrastructure and other production deployment scenarios where OS based tracing mechanism is preferred (especially ETW on Windows).  

Depending on improvements in the general tracing infrastructure there might be a need for a secondary route which would write the tracing into a file (probably triggered by an environment variable). Currently it seems that in order to provide the best value with this feature we need a very easy way to consume the traces which works everywhere. Humanly readable text files are a great fit for this.

## What's still missing
TODOs for this document:
* Native dependency binder - should the same mechanism be used for native dependency resolution (PInvoke)? If so:
    * DllMap interactions?
    * Is there interaction between managed resolution and native resolution?
    * Describe scenarios
* Reentracy problem - using tracing in binding code can mean new binding events inside the tracing implementation. How could this be handled and is this a big problem?