/******************************************************************************
 *
 * FILE          : UserRoles.cs
 * PROJECT       : CollectIQ
 * PROGRAMMER    : Darryl Poworoznyk
 * FIRST VERSION : 2026-06-02
 *
 * DESCRIPTION:
 *
 * This file contains string constants for CollectIQ user roles.
 *
 * These values are useful when storing roles in SQLite, sending roles to the
 * API, or comparing role names in a consistent way.
 *
 * CHANGE LOG:
 *
 * Date         Programmer              Description
 * ----------   --------------------    ---------------------------------------
 * 2026-06-02   Darryl Poworoznyk       Rebuilt role constants for long-term
 *                                      CollectIQ identity architecture.
 *
 *****************************************************************************/

namespace CollectIQ.Models
{
    /// <summary>
    /// Stores user role string constants.
    /// </summary>
    public static class UserRoles
    {
        #region Public Constants

        /// <summary>
        /// Standard user role.
        /// </summary>
        public const string Regular = "Regular";

        /// <summary>
        /// Paid subscriber role.
        /// </summary>
        public const string Subscriber = "Subscriber";

        /// <summary>
        /// Moderator role.
        /// </summary>
        public const string Moderator = "Moderator";

        /// <summary>
        /// Administrator role.
        /// </summary>
        public const string Admin = "Admin";

        #endregion

        #region Public Methods

        /******************************************************************************
         *
         * METHOD      : FromEnum
         *
         * DESCRIPTION :
         *
         * Converts a UserRole enum value into the matching role string.
         *
         * PARAMETERS  :
         *
         * role - The UserRole enum value to convert.
         *
         * RETURNS:
         *
         * The string representation of the user role.
         *
         *****************************************************************************/
        public static string FromEnum(UserRole role)
        {
            return role switch
            {
                UserRole.Admin => Admin,
                UserRole.Moderator => Moderator,
                UserRole.Subscriber => Subscriber,
                _ => Regular
            };
        }

        /******************************************************************************
         *
         * METHOD      : ToEnum
         *
         * DESCRIPTION :
         *
         * Converts a role string into the matching UserRole enum value.
         *
         * PARAMETERS  :
         *
         * role - The role string to convert.
         *
         * RETURNS:
         *
         * The matching UserRole value.
         *
         *****************************************************************************/
        public static UserRole ToEnum(string role)
        {
            if (string.Equals(role, Admin, StringComparison.OrdinalIgnoreCase))
            {
                return UserRole.Admin;
            }

            if (string.Equals(role, Moderator, StringComparison.OrdinalIgnoreCase))
            {
                return UserRole.Moderator;
            }

            if (string.Equals(role, Subscriber, StringComparison.OrdinalIgnoreCase))
            {
                return UserRole.Subscriber;
            }

            return UserRole.Regular;
        }

        #endregion
    }
}