using LabelMaker.Core.Data;
using LabelMaker.Core.Sequences;

namespace LabelMaker.Cli.Commands;

internal static class SequenceCommand
{
    public static int Run(ArgMap o)
    {
        SequenceDefinition def = CliShared.BuildSequence(o);
        (string first, string last, long count) = SequenceGenerator.Preview(def);

        if (count == 0)
        {
            throw new CliException("The sequence produces no values (check start/end/step).");
        }

        Console.Error.WriteLine($"{count:N0} values · first={first} · last={last}");

        bool checkDupes = o.GetFlag("check-duplicates");
        var values = new List<string>();
        TextWriter output = Console.Out;
        StreamWriter? file = null;
        if (o.Has("out"))
        {
            file = new StreamWriter(o.GetRequired("out"), append: false);
            output = file;
        }

        try
        {
            foreach (string value in SequenceGenerator.Enumerate(def))
            {
                output.WriteLine(value);
                if (checkDupes)
                {
                    values.Add(value);
                }
            }
        }
        finally
        {
            file?.Dispose();
        }

        if (checkDupes)
        {
            DuplicateReport report = DuplicateDetector.Check(values, DuplicatePolicy.Warn);
            if (report.HasDuplicates)
            {
                foreach (string line in report.Describe("value"))
                {
                    Console.Error.WriteLine(line);
                }

                return 3;
            }

            Console.Error.WriteLine("No duplicates.");
        }

        return 0;
    }
}
