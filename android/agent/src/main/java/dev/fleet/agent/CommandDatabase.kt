package dev.fleet.agent

import android.content.Context
import androidx.room.Dao
import androidx.room.Database
import androidx.room.Entity
import androidx.room.ForeignKey
import androidx.room.Index
import androidx.room.Insert
import androidx.room.PrimaryKey
import androidx.room.Query
import androidx.room.Room
import androidx.room.RoomDatabase

@Entity(tableName = "command_inbox", indices = [Index(value = ["deviceId", "sequence"], unique = true)])
internal data class CommandInboxRow(
    @PrimaryKey val commandId: String,
    val deviceId: String,
    val sequence: Long,
    val canonicalJson: String,
    val receiptEventId: String,
    val initialState: String,
)

@Entity(tableName = "command_event_outbox", foreignKeys = [ForeignKey(
    entity = CommandInboxRow::class, parentColumns = ["commandId"], childColumns = ["commandId"],
    onDelete = ForeignKey.RESTRICT,
)], indices = [Index("commandId"), Index("deviceId")])
internal data class CommandEventRow(
    @PrimaryKey val eventId: String,
    val commandId: String,
    val deviceId: String,
    val wireJson: String,
)

@Dao
internal interface CommandJournalDao {
    @Query("SELECT * FROM command_inbox WHERE commandId = :commandId")
    fun command(commandId: String): CommandInboxRow?

    @Query("SELECT * FROM command_inbox WHERE deviceId = :deviceId AND sequence = :sequence")
    fun sequence(deviceId: String, sequence: Long): CommandInboxRow?

    @Insert
    fun insertCommand(command: CommandInboxRow)

    @Insert
    fun insertEvent(event: CommandEventRow)

    @Query("SELECT * FROM command_event_outbox WHERE deviceId = :deviceId ORDER BY rowid LIMIT :limit")
    fun pendingEvents(deviceId: String, limit: Int): List<CommandEventRow>

    @Query("SELECT * FROM command_event_outbox WHERE deviceId = :deviceId AND eventId = :eventId")
    fun event(deviceId: String, eventId: String): CommandEventRow?

    @Query("DELETE FROM command_event_outbox WHERE deviceId = :deviceId AND eventId = :eventId AND commandId = :commandId AND wireJson = :wireJson")
    fun deleteUploadedEvent(deviceId: String, eventId: String, commandId: String, wireJson: String): Int
}

@Database(entities = [CommandInboxRow::class, CommandEventRow::class], version = 1, exportSchema = true)
internal abstract class CommandDatabase : RoomDatabase() {
    abstract fun journal(): CommandJournalDao

    companion object {
        fun open(context: Context): CommandDatabase = Room.databaseBuilder(
            context.applicationContext, CommandDatabase::class.java, "fleet-command-journal.db")
            .setJournalMode(JournalMode.WRITE_AHEAD_LOGGING)
            .build()
    }
}