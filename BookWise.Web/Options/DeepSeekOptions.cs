using System.ComponentModel.DataAnnotations;

namespace BookWise.Web.Options;

public class DeepSeekOptions
{
    [Required]
    [Url]
    public string Endpoint { get; set; } = string.Empty;

    [Required]
    public string ApiKey { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string Model { get; set; } = "deepseek-chat";

    [Range(1, 20)]
    public int RecommendationCount { get; set; } = 6;

    [Range(1, 2000)]
    public int MaxAuthorContextCount { get; set; } = 50;
}
