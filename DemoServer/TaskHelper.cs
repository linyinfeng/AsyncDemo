using System;
using System.Threading.Tasks;

namespace DemoServer
{
    public static class TaskHelper
    {
        public static async void FireAndForget(this Task task, Action<Exception> onException = null)
        {
            try
            {
                await task;
            }
            catch (Exception e)
            {
                onException?.Invoke(e);
            }
        }
    }
}