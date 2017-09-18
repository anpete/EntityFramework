using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ConsoleApp1
{
    public interface IMyDbContext
    {
        DbSet<Foo> Foos { get; set; }
    }

    public class MyDbContext : DbContext, IMyDbContext
    {
        public MyDbContext(DbContextOptions<MyDbContext> options)
            : base(options)
        {
        }

        public DbSet<Foo> Foos { get; set; }
    }

    public class Foo
    {
        public int Id { get; set; }
    }

    public class Program
    {
        public static void Main()
        {
            var services = new ServiceCollection()
                .AddScoped<IMyDbContext>(provider => provider.GetService<MyDbContext>())
                .AddDbContextPool<MyDbContext>(options =>
                {
                    options.UseSqlServer(
                        @"Server=(localdb)\mssqllocaldb;Database=Test;Trusted_Connection=True;ConnectRetryCount=0");
                })
                .BuildServiceProvider();

            var scope0 = services.CreateScope();
            Console.WriteLine($"Acquired _serviceScope: ({scope0.GetHashCode()})");

            try
            {
                Console.WriteLine($"scope0.GetService<IMyDbContext>() (Thread: {Thread.CurrentThread.ManagedThreadId})");
                var context = (DbContext)scope0.ServiceProvider.GetService<IMyDbContext>();

                Console.WriteLine($"Deleting database. (Thread: {Thread.CurrentThread.ManagedThreadId})");
                context.Database.EnsureDeleted();

                Console.WriteLine($"Creating database. (Thread: {Thread.CurrentThread.ManagedThreadId})");
                context.Database.EnsureCreated();
            }
            finally
            {
                Console.WriteLine($"scope0.Dispose() ({scope0.GetHashCode()}) (Thread: {Thread.CurrentThread.ManagedThreadId})");
                scope0.Dispose();
            }

            Parallel.For(0, 50, s =>
            {
                var scope = services.CreateScope();
                Console.WriteLine($"Acquired _serviceScope: ({scope.GetHashCode()})");

                try
                {
                    Console.WriteLine($"scope.GetService<IMyDbContext>() (Thread: {Thread.CurrentThread.ManagedThreadId})");
                    var context = scope.ServiceProvider.GetService<IMyDbContext>();

                    context.Foos.Add(new Foo());

                    ((DbContext) context).SaveChanges();

                    var _ = context.Foos.ToList();
                }
                finally
                {
                    Console.WriteLine($"scope.Dispose() ({scope.GetHashCode()}) (Thread: {Thread.CurrentThread.ManagedThreadId})");
                    scope.Dispose();
                }
            });

            services.Dispose();
        }
    }
}