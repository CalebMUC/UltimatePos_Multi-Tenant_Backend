using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UltimatePos.Domain.Exceptions
{
    public class InvalidCredentialsException : Exception
    {
        public InvalidCredentialsException() : base("Invalid credentials provided.") { }
       
    }
    public class UserNotFoundException : Exception
    {
        public UserNotFoundException() : base("User not found.") { }
    }
    public class UserAlreadyExistsException : Exception
    {
        public UserAlreadyExistsException() : base("User already exists.") { }
    }

    public class NotFoundException : Exception
    {
        public NotFoundException(string message) : base(message) { }
    }

    public class InvalidAssignmentException : Exception
    {
        public InvalidAssignmentException(string message) : base(message) { }
    }

    public class DuplicateBusinessException : Exception
    {
        public DuplicateBusinessException(string message) : base(message) { }
    }

    public class InsufficientStockException : Exception
    {
        public InsufficientStockException(string message) : base(message) { }
    }

    public class PurchaseNotAllowedException : Exception
    {
        public PurchaseNotAllowedException(string message) : base(message) { }
    }

    /// <summary>The caller is signed in but may not act on this business or record (HTTP 403).</summary>
    public class ForbiddenException : Exception
    {
        public ForbiddenException(string message) : base(message) { }
    }

    /// <summary>One or more fields are invalid. Carries field-keyed messages so a client can show each beside its input (HTTP 400).</summary>
    public class ValidationException : Exception
    {
        public IReadOnlyDictionary<string, string[]> Errors { get; }

        public ValidationException(IReadOnlyDictionary<string, string[]> errors)
            : base(errors.SelectMany(e => e.Value).FirstOrDefault() ?? "Validation failed.")
            => Errors = errors;

        public ValidationException(string field, string message) : this(new Dictionary<string, string[]> { [field] = new[] { message } }) { }
    }

    /// <summary>A credit sale would take the customer past their credit limit (HTTP 409).</summary>
    public class CreditLimitExceededException : Exception
    {
        public CreditLimitExceededException(string message) : base(message) { }
    }

    /// <summary>A prescription-only product is being sold without the prescription details (HTTP 422).</summary>
    public class PrescriptionRequiredException : Exception
    {
        public PrescriptionRequiredException(string message) : base(message) { }
    }

    /// <summary>A sign-in or refresh token is missing, expired or revoked (HTTP 401).</summary>
    public class InvalidTokenException : Exception
    {
        public InvalidTokenException(string message) : base(message) { }
    }
}
