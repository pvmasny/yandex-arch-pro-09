using System.ComponentModel.DataAnnotations;

namespace BionicProAuth.Models
{
    public class LoginRequest
    {
        [Required]
        public required string Username { get; set; }

        [Required]
        public required string Password { get; set; }
    }
}
