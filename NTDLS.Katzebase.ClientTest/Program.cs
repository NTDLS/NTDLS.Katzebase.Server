﻿using NTDLS.Katzebase.Client;
using NTDLS.Katzebase.Client.Payloads;

namespace NTDLS.Katzebase.ClientTest
{
    internal class Program
    {
        static void Main()
        {
            var threads = new List<Thread>()
            {
                new Thread(InsertUsingAPI),
                new Thread(InsertUsingQueries),
            };

            threads.ForEach(t => t.Start());
            threads.ForEach(t => t.Join());

            Console.WriteLine("Complete.");

            Console.ReadLine();
        }

        public static void InsertUsingAPI()
        {
            using var client = new KbClient("http://localhost:6858");

            string schemaName = "ClientTest:B";
            int id = 0;

            client.Schema.DropIfExists(schemaName);
            client.Schema.Create(schemaName);

            client.Transaction.Begin();

            for (int s = 0; s < 10; s++)
            {
                for (int i = 0; i < 100; i++)
                {
                    Console.WriteLine($"InsertUsingAPI: {id}");

                    client.Document.Store(schemaName, new
                    {
                        Id = id++,
                        SegmentA = i,
                        SegmentB = s,
                        UUID = Guid.NewGuid()
                    });
                }
            }
            client.Transaction.Commit();

            client.Schema.Indexes.Create(schemaName, new KbUniqueKey("IX_UUID", "UUID"));
            client.Schema.Indexes.Create(schemaName, new KbUniqueKey("IX_ID", "Id"));
            client.Schema.Indexes.Create(schemaName, new KbIndex("IX_Segments", "SegmentA,SegmentB"));
        }

        public static void InsertUsingQueries()
        {
            using var client = new KbClient("http://localhost:6858");
            string schemaName = "ClientTest:A";
            int id = 0;

            client.Schema.DropIfExists(schemaName);
            client.Schema.Create(schemaName);

            client.Transaction.Begin();
            for (int s = 0; s < 10; s++)
            {
                for (int i = 0; i < 100; i++)
                {
                    Console.WriteLine($"InsertUsingQueries: {id}");
                    client.Query.ExecuteQuery($"INSERT INTO {schemaName} (Id = {id++}, SegmentA = {i}, SegmentB = {s}, UUID = '{Guid.NewGuid()}')");
                }
            }
            client.Transaction.Commit();

            client.Query.ExecuteQuery($"CREATE UniqueKey IX_UUID (UUID) ON {schemaName}");
            client.Query.ExecuteQuery($"CREATE UniqueKey IX_ID (Id) ON {schemaName}");
            client.Query.ExecuteQuery($"CREATE INDEX IX_Segments (SegmentA, SegmentB) ON {schemaName}");
        }
    }
}