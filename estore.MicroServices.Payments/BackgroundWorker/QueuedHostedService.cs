using Microsoft.Extensions.Hosting;

namespace estore.MicroServices.Payments.BackgroundWorker
{
    public class QueuedHostedService : BackgroundService
    {
        public IBackgroundTaskQueue TaskQueue { get; }

        public QueuedHostedService(IBackgroundTaskQueue taskQueue)
        {
            TaskQueue = taskQueue;
        }

        protected async override Task ExecuteAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var workItem = await TaskQueue.DequeueAsync(cancellationToken);

                try
                {
                    await workItem(cancellationToken);
                }
                catch (Exception)
                {
                }
            }
        }
    }
}
