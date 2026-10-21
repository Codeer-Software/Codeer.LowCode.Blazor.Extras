using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.Extras.Designs;

namespace Codeer.LowCode.Blazor.Extras.Test.DesignCheck
{
    public class JobTriggerFieldDesignCheckTest
    {
        [Test]
        public void ジョブ名が空なら指摘される()
        {
            var (d, mod) = Utilities.CreateDesignData();
            var field = new JobTriggerFieldDesign { Name = "Job" };
            mod.Fields.Add(field);

            var ret = field.CheckDesign(new DesignCheckContext(mod.Name, d, Utilities.CreateDataSource()));

            Assert.That(ret.Count, Is.EqualTo(1));
            ret[0].AssertFieldLocation(mod.Name, "Job", nameof(JobTriggerFieldDesign.JobName));
            Assert.That(ret[0].Code, Is.EqualTo(DesignCheckCode.Create(typeof(JobTriggerFieldDesign), 1)));
        }
    }
}
