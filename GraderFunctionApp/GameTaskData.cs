using System.Runtime.Serialization;

namespace GraderFunctionApp
{
    [DataContract]
    public class GameTaskData
    {
        [DataMember] public int GameClassOrder { get; set; }
        [DataMember] public required string Name { get; set; }

        [DataMember] public required string[] Tests { get; set; }
        [DataMember] public required string Instruction { get; set; }
        [DataMember] public required string Filter { get; set; }
        [DataMember] public int TimeLimit { get; set; }
        [DataMember] public int Reward { get; set; }

        public override string ToString()
        {
            return Name + "," + GameClassOrder + "," + TimeLimit + "," + Reward + "," + Filter + "=>" + Instruction.Substring(0, 30);
        }
    }


}
