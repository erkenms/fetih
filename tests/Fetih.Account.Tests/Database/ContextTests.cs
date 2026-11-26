namespace Fetih.Account.Database.Tests
{
    using System;
    using Xunit;
    using Fetih.Account.Database;

    public class ContextTests
    {
        [Theory]
        [InlineData("--Database:Hostname=mysql")]
        public void ContextConfiguration(params string[] args)
        {
            var config = new ServerConfiguration(args);
            ServerDbContext.Configuration = config.Database;
            try 
            {
                using var db = new ServerDbContext();
            }
            catch (Exception ex) { Assert.Null(ex); }
        }
    }
}
