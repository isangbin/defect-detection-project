using Postgrest.Attributes;
using Postgrest.Models;

namespace EggClassifier.Models.Database;

/// <summary>
/// Supabase users 테이블 엔티티
/// </summary>
[Table("users")]
public class UserEntity : BaseModel
{
    [PrimaryKey("idx", false)]
    public int Idx { get; set; }

    [Column("user_id")]
    public string UserId { get; set; } = string.Empty;

    [Column("user_password")]
    public string UserPassword { get; set; } = string.Empty;

    [Column("user_name")]
    public string? UserName { get; set; }

    [Column("user_face")]
    public float[] UserFace { get; set; } = Array.Empty<float>();

    [Column("user_role")]
    public string? UserRole { get; set; }
}
