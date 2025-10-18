//
//  FILE            : SqliteDatabase.cs
//  PROJECT         : CollectIQ (Mobile)
//  PROGRAMMER      : <your name>
//  FIRST VERSION   : 2025-10-18
//  DESCRIPTION     :
//      SQLite-backed implementation of IDatabase using sqlite-net-pcl.
//      Database file is created under FileSystem.AppDataDirectory.
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CollectIQ.Interfaces;
using CollectIQ.Models;
using Microsoft.Maui.Storage;
using SQLite;

namespace CollectIQ.Services
{
    /// <summary>
    /// Concrete SQLite implementation of IDatabase.
    /// </summary>
    public class SqliteDatabase : IDatabase
    {
        private const string DatabaseFileName = "collectiq.db3";
        private SQLiteAsyncConnection _connection;

        /// <summary>
        /// Absolute path to the SQLite database file.
        /// </summary>
        public string DbPath { get; }

        /// <summary>
        /// Initializes a new instance of the SqliteDatabase class.
        /// </summary>
        public SqliteDatabase()
        {
            DbPath = Path.Combine(FileSystem.AppDataDirectory, DatabaseFileName);
        }

        /// <summary>
        /// Lazily initializes the SQLite connection and ensures schema exists.
        /// </summary>
        /// <returns>Task that completes when initialization is finished.</returns>
        public async Task InitAsync()
        {
            if (_connection != null)
            {
                return;
            }

            _connection = new SQLiteAsyncConnection(
                DbPath,
                SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache
            );

            await _connection.CreateTableAsync<Card>();
        }

        /// <summary>
        /// Returns all cards ordered by UpdatedAt (descending). If query is provided,
        /// performs a substring match on Player, Set, Team, or Number.
        /// </summary>
        /// <param name="query">Optional filter text. If null/empty, returns all cards.</param>
        /// <returns>List of cards matching the filter (or all cards).</returns>
        public async Task<List<Card>> GetCardsAsync(string query = null)
        {
            await InitAsync();

            if (string.IsNullOrWhiteSpace(query))
            {
                return await _connection
                    .Table<Card>()
                    .OrderByDescending(c => c.UpdatedAt)
                    .ToListAsync();
            }

            string trimmed = query.Trim();

            return await _connection
                .Table<Card>()
                .Where(c =>
                    c.Player.Contains(trimmed) ||
                    c.Set.Contains(trimmed) ||
                    c.Team.Contains(trimmed) ||
                    c.Number.Contains(trimmed))
                .OrderByDescending(c => c.UpdatedAt)
                .ToListAsync();
        }

        /// <summary>
        /// Inserts a new row when Id == 0, otherwise updates the existing row.
        /// </summary>
        /// <param name="card">Card entity to save.</param>
        /// <returns>The saved card instance.</returns>
        public async Task<Card> UpsertAsync(Card card)
        {
            await InitAsync();

            card.UpdatedAt = DateTime.UtcNow;

            if (card.Id == 0)
            {
                await _connection.InsertAsync(card);
            }
            else
            {
                await _connection.UpdateAsync(card);
            }

            return card;
        }

        /// <summary>
        /// Deletes the specified Card entity.
        /// </summary>
        /// <param name="card">Entity to delete.</param>
        /// <returns>Number of rows affected.</returns>
        public async Task<int> DeleteAsync(Card card)
        {
            await InitAsync();
            return await _connection.DeleteAsync(card);
        }
    }
}
