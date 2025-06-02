using BulkSMPosting.VM;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Protocols;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BulkSMPosting.Logics.BLL
{
    public class Helper
    {
        public static void InsertLogException(Exception exc, IConfiguration configuration, string source = null)
        {
            LosCodeExceptionLogVM losCodeExceptionLog = new LosCodeExceptionLogVM();

            if (exc.InnerException != null)
            {
                losCodeExceptionLog.InnerExeType = exc.InnerException.GetType().ToString().Replace("'", "_");
                losCodeExceptionLog.InnerExeMessage = exc.InnerException.Message.Replace("'", "_");
                losCodeExceptionLog.InnerExeSource = exc.InnerException.Source?.Replace("'", "_");
                losCodeExceptionLog.InnerExeStackTrace = exc.InnerException.StackTrace?.Replace("'", "_");
            }

            losCodeExceptionLog.ExeType = exc.GetType().ToString().Replace("'", "_");
            losCodeExceptionLog.ExeMessage = exc.Message.Replace("'", "_");
            losCodeExceptionLog.ExeStackTrace = exc.StackTrace?.Replace("'", "_");
            losCodeExceptionLog.ExeSource = source;

            string query = @"INSERT INTO LosCodeExceptionLogs 
               (ExeSource,ExeType,ExeMessage,ExeStackTrace,InnerExeSource,InnerExeType,InnerExeMessage,InnerExeStackTrace,CreationDate)
                VALUES(@ExeSource, @ExeType, @ExeMessage, @ExeStackTrace, @InnerExeSource, @InnerExeType, @InnerExeMessage, @InnerExeStackTrace, GETDATE())";

            using (var con = new SqlConnection(configuration.GetConnectionString("defaultconnectionS")))
            using (var cmd = new SqlCommand(query, con))
            {
                cmd.CommandType = CommandType.Text;
                cmd.Parameters.AddWithValue("@ExeSource", losCodeExceptionLog.ExeSource ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@ExeType", losCodeExceptionLog.ExeType ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@ExeMessage", losCodeExceptionLog.ExeMessage ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@ExeStackTrace", losCodeExceptionLog.ExeStackTrace ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@InnerExeSource", losCodeExceptionLog.InnerExeSource ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@InnerExeType", losCodeExceptionLog.InnerExeType ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@InnerExeMessage", losCodeExceptionLog.InnerExeMessage ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@InnerExeStackTrace", losCodeExceptionLog.InnerExeStackTrace ?? (object)DBNull.Value);

                con.Open();
                cmd.ExecuteNonQuery();
            }
        }
    }
}
