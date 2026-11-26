namespace Fetih.Core.Tests
{
    using System.Linq;
    using Fetih.Core.Database;
    using Xunit;

    public class SqlSplitTests
    {
        [Fact]
        public void SplitsAtSemicolonsOutsideQuotesAndComments()
        {
            var sql = "-- header; not a statement\nCREATE TABLE `a;b` (x varchar(9) DEFAULT 'it''s; ok');\n" +
                "/* skipped; */ INSERT INTO t VALUES (\"a;\\\"b\");\n/*!40101 SET NAMES utf8 */;\nSELECT 1";
            Assert.Equal(new[]
            {
                "CREATE TABLE `a;b` (x varchar(9) DEFAULT 'it''s; ok')",
                "INSERT INTO t VALUES (\"a;\\\"b\")",
                "/*!40101 SET NAMES utf8 */",
                "SELECT 1"
            }, SchemaMigrator.Split(sql).ToArray());
        }

        [Theory]
        [InlineData("0001-add-column.sql", 1, "add-column")]
        [InlineData("0042-x.sql", 42, "x")]
        public void ParsesFileNames(string file, int id, string name)
        {
            var migration = SchemaMigrator.Parse(file, "SELECT 1");
            Assert.Equal(id, migration.Id);
            Assert.Equal(name, migration.Name);
        }

        [Theory]
        [InlineData("0000-baseline.sql")]
        [InlineData("add-column.sql")]
        [InlineData("1-x.sql")]
        public void RejectsBadFileNames(string file) =>
            Assert.Throws<MigrationException>(() => SchemaMigrator.Parse(file, "SELECT 1"));

        [Fact]
        public void ChecksumIgnoresLineEndings() =>
            Assert.Equal(new Migration(1, "a", "A\nB").Checksum, new Migration(1, "a", "A\r\nB").Checksum);
    }
}
