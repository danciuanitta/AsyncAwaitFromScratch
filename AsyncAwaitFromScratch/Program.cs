using System.Collections.Concurrent;

//this to work you need to use again at the begining, with version 1 the ThreadPool class and delegate instead of lambda
//with first custom implementation this was not working, was displaying only 0, because we weren't capturing the current context
AsyncLocal<int> myValue = new();
for (int i = 0; i < 30; i++)
{
    myValue.Value = i;
    //int j = i;
    //ThreadPool.QueueUserWorkItem(delegate //version0: delegate instead of lambda function
    MyThreadPool.QueueUserWorkItem(() =>
    {
        //version 0: Console.WriteLine(i); ==> not working; displaying only 30 because of closure, so you need the j variable
        //Console.WriteLine(j);
        Console.WriteLine(myValue.Value);
        Thread.Sleep(1000);
    });
}

Console.ReadLine();


//not make class public to be sure never anybody tries to also lock it. using lock is wrong because it touches private state, but if it is a private class that never be touched it is ok for this tutorial
class MyTask
{
    //version1
    private bool _isCompleted;
    private Exception? _exception;
    private Action? _continuation;
    private ExecutionContext? _context;

    public bool IsCompleted
    {
        get
        {
            //needs to be thread safe  not the recommandet way, but easiest for this tutorial
            lock (this)
            {
                return _isCompleted;
            }
        }

    }

    //these set methods in .net are actually in taskCompletionResult so you don't have to actually set them on a task, but are set for you
    public void SetResult() => Complete(null);

    public void SetException(Exception exception) => Complete(exception);

    private void Complete(Exception? exception)
    {
        lock (this)
        {
            if (_isCompleted) throw new InvalidOperationException("Stop messing up my code");

            _isCompleted = true;
            _exception = exception;

            if (_continuation is not null)
            {
                MyThreadPool.QueueUserWorkItem(delegate
                {
                    if (_context is null)
                    {
                        _continuation();
                    }
                    else
                    {
                        ExecutionContext.Run(_context, (object? state) => ((Action)state!).Invoke(), _continuation);
                    }
                });
            }
        }
    }

    public void Wait()
    { }

    //invoke when it completes
    public MyTask ContinueWith(Action action)
    {
        //version 2
        MyTask t = new();

        Action callback = () =>
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                t.SetException(e);
                return;
            }
            t.SetResult();
        };

        lock (this)
        {
            if (_isCompleted)
            {
                MyThreadPool.QueueUserWorkItem(callback);
            }
            else
            {
                _continuation = callback;
                _context = ExecutionContext.Capture();
            }
        }

        return t;
    }
}

public static class MyThreadPool
{
    private static readonly BlockingCollection<(Action, ExecutionContext?)> s_WorkItems = new();

    public static void QueueUserWorkItem(Action action) => s_WorkItems.Add((action, ExecutionContext.Capture()));

    static MyThreadPool()
    {
        for (int i = 0; i < Environment.ProcessorCount; i++)
        {
            new Thread(() =>
            {
                while (true)
                {
                    (Action workItem, ExecutionContext? context) = s_WorkItems.Take();
                    if (context is null)
                    {
                        workItem();
                    }
                    else
                    {
                        //ExecutionContext.Run(context, delegate { workItem(); }, null);  => invoke this delegate, which is the workItem passed, using this context
                        ExecutionContext.Run(context, (object? state) => ((Action)state!).Invoke(), workItem); //here state argument is passed into that context callback delegate 
                    }
                }
            })
            { IsBackground = true }.Start();
        }
    }

    //version1 

    //private static readonly BlockingCollection<Action> s_WorkItems = new();

    //public static void QueueUserWorkItem(Action action) => s_WorkItems.Add((action));

    //static MyThreadPool()
    //{
    //    for (int i = 0; i < Environment.ProcessorCount; i++)
    //    {
    //        new Thread(() =>
    //        {
    //            while (true)
    //            {
    //                Action workItem = s_WorkItems.Take();
    //                workItem();
    //            }
    //        })
    //        { IsBackground = true }.Start();
    //    }
    //}
}