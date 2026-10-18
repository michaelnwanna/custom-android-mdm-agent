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
import androidx.room.Update
import java.time.Instant
import java.util.UUID

@Entity(
    tableName = "policies",
    indices = [Index("policyId", unique = true), Index("isActive")]
)
internal data class PolicyRow(
    @PrimaryKey val policyId: String,
    val assignedVersion: Long,
    val acknowledgedVersion: Long?,
    val policyJson: String,
    val isActive: Boolean = true,
    val assignedAt: String, // ISO-8601 Instant
    val acknowledgedAt: String?, // ISO-8601 Instant
)

@Dao
internal interface PolicyDao {
    @Query("SELECT * FROM policies WHERE policyId = :policyId")
    fun policy(policyId: String): PolicyRow?

    @Query("SELECT * FROM policies WHERE isActive = 1 ORDER BY assignedAt DESC LIMIT 1")
    fun currentPolicy(): PolicyRow?

    @Query("SELECT * FROM policies WHERE isActive = 1")
    fun activePolicies(): List<PolicyRow>

    @Insert
    fun insertPolicy(policy: PolicyRow)

    @Update
    fun updatePolicy(policy: PolicyRow): Int

    @Query("UPDATE policies SET isActive = 0 WHERE policyId = :policyId")
    fun deactivatePolicy(policyId: String): Int

    @Query("DELETE FROM policies WHERE policyId = :policyId")
    fun deletePolicy(policyId: String): Int
}

@Database(entities = [PolicyRow::class], version = 1, exportSchema = true)
internal abstract class PolicyDatabase : RoomDatabase() {
    abstract fun policies(): PolicyDao

    companion object {
        fun open(context: Context): PolicyDatabase = Room.databaseBuilder(
            context.applicationContext, PolicyDatabase::class.java, "fleet-policies.db")
            .setJournalMode(JournalMode.WRITE_AHEAD_LOGGING)
            .build()
    }
}
