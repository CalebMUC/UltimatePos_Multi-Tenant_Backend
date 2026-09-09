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
}
