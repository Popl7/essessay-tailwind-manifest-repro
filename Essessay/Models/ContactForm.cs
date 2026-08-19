using System.ComponentModel.DataAnnotations;

namespace Essessay.Models;

// Bound from a real <form> post (contentType: 'form'), not from signals —
// so the same model works for a Datastar fetch and a plain browser submit.
public class ContactForm
{
    [Required]
    [StringLength(60)]
    [Display(Name = "Your name")]
    public string? Name { get; set; }

    [Required]
    [EmailAddress]
    public string? Email { get; set; }

    [Required]
    [StringLength(500, MinimumLength = 10, ErrorMessage = "Give us at least 10 characters.")]
    public string? Message { get; set; }

    // Datastar appends the submitting button's name/value to the form data,
    // so the server can tell which button sent the form.
    public string? Source { get; set; }
}
