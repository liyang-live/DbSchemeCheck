﻿using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;

namespace DbSchemeCheck
{
    public class DbHelper
    {
        #region GetDbTables
        
        public static List<DbTable> GetDbTables(string connectionString, string database, string tables = null)
        {
            if (!string.IsNullOrEmpty(tables))
            {
                tables = $" and obj.name in ('{tables.Replace(",", "','")}')";
            }
            #region SQL
            var sql = string.Format(@"SELECT
                                    obj.name tablename,
                                    schem.name schemname,
                                    idx.rows,
                                    CAST
                                    (
                                        CASE 
                                            WHEN (SELECT COUNT(1) FROM sys.indexes WHERE object_id= obj.OBJECT_ID AND is_primary_key=1) >=1 THEN 1
                                            ELSE 0
                                        END 
                                    AS BIT) HasPrimaryKey                                         
                                    from {0}.sys.objects obj 
                                    inner join {0}.dbo.sysindexes idx on obj.object_id=idx.id and idx.indid<=1
                                    INNER JOIN {0}.sys.schemas schem ON obj.schema_id=schem.schema_id
                                    where type='U' {1}
                                    order by obj.name", database, tables);
            #endregion
            var dt = GetDataTable(connectionString, sql);
            return dt.Rows.Cast<DataRow>().Select(row => new DbTable
            {
                TableName = row.Field<string>("tablename"),
                SchemaName = row.Field<string>("schemname"),
                Rows = row.Field<int>("rows"),
                HasPrimaryKey = row.Field<bool>("HasPrimaryKey")
            }).ToList();
        }
        #endregion

        #region GetDbColumns
        
        public static List<DbColumn> GetDbColumns(string connectionString, string database, string tableName, string schema = "dbo")
        {
            #region SQL
            var sql = string.Format(@"
                                    WITH indexCTE AS
                                    (
                                        SELECT 
                                        ic.column_id,
                                        ic.index_column_id,
                                        ic.object_id    
                                        FROM {0}.sys.indexes idx
                                        INNER JOIN {0}.sys.index_columns ic ON idx.index_id = ic.index_id AND idx.object_id = ic.object_id
                                        WHERE  idx.object_id =OBJECT_ID(@tableName) AND idx.is_primary_key=1
                                    )
                                    select
                                    colm.column_id ColumnID,
                                    CAST(CASE WHEN indexCTE.column_id IS NULL THEN 0 ELSE 1 END AS BIT) IsPrimaryKey,
                                    colm.name ColumnName,
                                    systype.name ColumnType,
                                    colm.is_identity IsIdentity,
                                    colm.is_nullable IsNullable,
                                    cast(colm.max_length as int) ByteLength,
                                    (
                                        case 
                                            when systype.name='nvarchar' and colm.max_length>0 then colm.max_length/2 
                                            when systype.name='nchar' and colm.max_length>0 then colm.max_length/2
                                            when systype.name='ntext' and colm.max_length>0 then colm.max_length/2 
                                            else colm.max_length
                                        end
                                    ) CharLength,
                                    cast(colm.precision as int) Precision,
                                    cast(colm.scale as int) Scale,
                                    prop.value Remark
                                    from {0}.sys.columns colm
                                    inner join {0}.sys.types systype on colm.system_type_id=systype.system_type_id and colm.user_type_id=systype.user_type_id
                                    left join {0}.sys.extended_properties prop on colm.object_id=prop.major_id and colm.column_id=prop.minor_id
                                    LEFT JOIN indexCTE ON colm.column_id=indexCTE.column_id AND colm.object_id=indexCTE.object_id                                        
                                    where colm.object_id=OBJECT_ID(@tableName)
                                    order by colm.column_id", database);
            #endregion
            var param = new SqlParameter("@tableName", SqlDbType.NVarChar, 100) { Value = string.Format("{0}.{1}.{2}", database, schema, tableName) };
            var dt = GetDataTable(connectionString, sql, param);
            return dt.Rows.Cast<DataRow>().Select(row => new DbColumn()
            {
                ColumnID = row.Field<int>("ColumnID"),
                IsPrimaryKey = row.Field<bool>("IsPrimaryKey"),
                ColumnName = row.Field<string>("ColumnName"),
                ColumnType = row.Field<string>("ColumnType"),
                IsIdentity = row.Field<bool>("IsIdentity"),
                IsNullable = row.Field<bool>("IsNullable"),
                ByteLength = row.Field<int>("ByteLength"),
                CharLength = row.Field<int>("CharLength"),
                Precision = row.Field<int>("Precision"),
                Scale = row.Field<int>("Scale"),
                Remark = row["Remark"].ToString()
            }).ToList();
        }
        #endregion     
        
        #region GetDataTable
        public static DataTable GetDataTable(string connectionString, string commandText, params SqlParameter[] parms)
        {
            using (var connection = new SqlConnection(connectionString))
            {
                var command = connection.CreateCommand();
                command.CommandText = commandText;
                command.Parameters.AddRange(parms);
                var adapter = new SqlDataAdapter(command);

                var dt = new DataTable();
                adapter.Fill(dt);

                return dt;
            }
        }
        #endregion
    }
}