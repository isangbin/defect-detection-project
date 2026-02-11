using Postgrest.Attributes;
using Postgrest.Models;

namespace EggClassifier.Models.Database;

/// <summary>
/// Supabase egg 테이블 엔티티
/// </summary>
[Table("egg")]
public class EggEntity : BaseModel
{
    [PrimaryKey("idx", false)]
    public int Idx { get; set; }

    [Column("user_id")]
    public string UserId { get; set; } = string.Empty;

    [Column("egg_class")]
    public int EggClass { get; set; }

    [Column("accuracy")]
    public double Accuracy { get; set; }

    [Column("inspect_date")]
    public DateTime InspectDate { get; set; }

    [Column("egg_image")]
    public byte[] EggImage { get; set; } = Array.Empty<byte>();
}
