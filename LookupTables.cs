namespace Program;

public class LookupTables
{
    public static class LookupTable
    {
        public static readonly int[] ShardLengths =
        [
            13, // shard 1
            14,
            15,
            16,
            17,
            18,
            19,
            20,
            21, 
            26, // shard 10
            29, // disaster 1
            33,
            33,
            33 // disaster 4
        ];
    }
}