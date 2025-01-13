# .NET CoreCLR EventPipe bug example

This is a basic project designed to reproduce an issue with the .NET Core EventPipe mechanism since .NET 6.0.

To run this code as-is, use a Linux host or container, install .NET 9.0, and then run `./run.sh`. It will run examples with multiple threads to demonstrate how increasing the number of spawned threads decreases EventPipe performance.

The gist of the tests is:

- Warm up JIT (in case it might affect results)
- Enable EventPipe tracing for exceptions
- __Measurement 1__:
    - Synthetically throw and catch 500,000 exceptions
    - (should result in 2,000,000 EventPipe events and ~110% CPU)
- Spawn `N` short-lived threads and wait for them to complete
    - (each thread throws an exception to add to the EventPipe thread session state list)
- __Measurement 2__:
    - Synthetically throw and catch 500,000 exceptions
    - (as previously-spawned threads increase, measured events should decrease, and CPU should trend towards ~195%)
- Reconfigure EventPipe with identical settings
    - This actually cleans up the old session and creates a new one, with a new thread session state list
- __Measurement 3__:
    - Synthetically throw and catch 500,000 exceptions
    - (should result in ~2,000,000 EventPipe events and ~110% CPU)
    
    
This demonstrates that during an EventPipe session, if threads are ever created, regardless of how long they run, they will permanently degrade performance for that EventPipe session going forwards.
