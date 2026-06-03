/******************************************************************************
 *
 * FILE          : UserRole.cs
 * PROJECT       : CollectIQ
 * PROGRAMMER    : Darryl Poworoznyk
 * FIRST VERSION : 2026-06-02
 *
 * DESCRIPTION:
 *
 * This file defines the supported CollectIQ user role values.
 *
 * User roles determine broad authorization levels inside the application.
 * More detailed permissions can be added later through role/privilege tables.
 *
 * CHANGE LOG:
 *
 * Date         Programmer              Description
 * ----------   --------------------    ---------------------------------------
 * 2026-06-02   Darryl Poworoznyk       Rebuilt role enum for long-term
 *                                      CollectIQ identity architecture.
 *
 *****************************************************************************/

namespace CollectIQ.Models
{
    /// <summary>
    /// Defines the supported CollectIQ user roles.
    /// </summary>
    public enum UserRole
    {
        /// <summary>
        /// Standard application user.
        /// </summary>
        Regular = 0,

        /// <summary>
        /// Paid subscriber.
        /// </summary>
        Subscriber = 1,

        /// <summary>
        /// Marketplace or content moderator.
        /// </summary>
        Moderator = 2,

        /// <summary>
        /// System administrator.
        /// </summary>
        Admin = 3
    }
}