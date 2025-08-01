using System.Data;
using System.Security.Claims;
using BulkSmPosting.Logics.BLL;
using BulkSMPosting.VM;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Protocols;

namespace BulkSMPosting.Logics.BLL
{
    public class SMBLL : BaseBLL
    {
        private readonly IConfiguration _configuration;
        SqlTransaction tr = null;
        public SMBLL(IConfiguration configuration)
        {
            _configuration = configuration;
        }
        public async Task SMPost()
        {
            string activeKey = _configuration.GetSection("ConnectionStrings:active").Value;
            string connectionString = _configuration.GetConnectionString(activeKey);

            using (SqlConnection con = new SqlConnection(connectionString))
            {
                await con.OpenAsync();

                try
                {
                    List<SMPostVM> obj = GetRecordsFromSMPost(con);

                    if (obj.Count == 0)
                    {
                        Console.WriteLine("No records to process.");
                        return;
                    }
                    SqlCommand cmd = new SqlCommand
                    {
                        CommandType = CommandType.Text,
                        Connection = con
                    };
                    cmd.Parameters.Add("@SmCode", SqlDbType.VarChar, 10);

                    string UserID = "11399";
                    List<string> smCodesForPdlShares = new List<string>();

                    foreach (var item in obj)
                    {
                        SqlTransaction tr = null;
                        try
                        {
                            string code = item.SMCode;
                            cmd.Parameters["@SmCode"].Value = code;

                            // Check if Fi_Id exists in PDLSBI..SBILoanDisbStatus
                            int result = 0;
                            using (SqlCommand checkCmd = new SqlCommand("SELECT 1 FROM PDLSBI..SBILoanDisbStatus WHERE Fi_Id = @fiId", con))
                            {
                                checkCmd.Parameters.Add("@fiId", SqlDbType.BigInt).Value = item.FiId;
                                object scalarResult = await checkCmd.ExecuteScalarAsync();
                                result = (scalarResult != null) ? Convert.ToInt32(scalarResult) : 0;
                            }

                            // Check if already posted in SM table
                            cmd.CommandText = @"SELECT 1 FROM SM WHERE CODE = @SmCode";
                            int IsPosted = Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0);

                            DateTime paymentDate = (DateTime)item.PaymentDate;

                            if (result > 0 || paymentDate != DateTime.MinValue && IsPosted == 0)
                            {
                                tr = con.BeginTransaction();
                                cmd.Transaction = tr;

                                if (paymentDate != DateTime.MinValue)
                                {
                                    UpdateInsDueDate(code, paymentDate, con, tr);
                                }

                                if (activeKey.Equals("defaultconnections"))
                                {
                                    Console.WriteLine($"Calling UpdateSmAndChq with code={code}, DTFin={paymentDate}, UserID={UserID}");
                                    UpdateSmAndChq(code, paymentDate, UserID, con, tr);

                                    Console.WriteLine("UpdateSmAndChq completed.");
                                }

                                smCodesForPdlShares.Add(code);
                                tr.Commit();
                                tr = null;
                                Console.WriteLine($"Transaction committed successfully for code: {code}");

                                if (IsSmCodeExistsInSmAndChq(code))
                                {
                                    if (activeKey.Equals("defaultconnection"))
                                    {
                                        UpdateSmAndChq(code, paymentDate, UserID, con, tr);
                                        UpdateStatusForSmPost(code, con, tr);
                                    }
                                }
                                //tr.Commit();                             
                            }
                            else
                            {
                                string remarks = null;

                                if (result == 0 && paymentDate == DateTime.MinValue)
                                    remarks = "Account Number and PaymentDate Not Found";
                                else if (result == 0)
                                    remarks = "Account Number Not Found";
                                else if (paymentDate == DateTime.MinValue)
                                    remarks = "PaymentDate is not found";

                                Console.WriteLine($"Skipping code {code}: {remarks}");
                            }
                        }
                        catch (Exception ex)
                        {
                            if (tr != null)
                            {
                                try { tr.Rollback(); } catch { }
                            }
                            Console.WriteLine($"Error processing SMCode {item.SMCode}: {ex.Message}");
                            Helper.InsertLogException(ex, _configuration, "SMPOST_Schedular");
                            throw;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Fatal error in SMPost: {ex.Message}");
                    Helper.InsertLogException(ex, _configuration, "SMPOST_Schedular");
                    throw;
                }
                finally
                {
                    if (con != null && con.State == ConnectionState.Open)
                        await con.CloseAsync();
                    con?.Dispose();
                }
            }
        }
        private void UpdateInsDueDate(string code, DateTime startDate, SqlConnection con, SqlTransaction trans)
        {
            List<DateTime> insDueDates = new List<DateTime>();
            DateTime dtFin = DateTime.MinValue;

            // Get the Dt_Fin and existing INS_DUE_DT values
            using (SqlCommand cmd = new SqlCommand("Usp_UpdateInsDueDate", con, trans))
            {
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.AddWithValue("@SmCode", code);

                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (dtFin == null)
                            //if (!reader.IsDBNull(0))
                            dtFin = reader.GetDateTime(0);

                        if (!reader.IsDBNull(1))
                            insDueDates.Add(reader.GetDateTime(1));
                    }
                }
            }
            //if (dtFin == DateTime.MinValue)
            //{
            //    Console.WriteLine("Dt_Fin not found for the given code.");
            //    return;
            //}

            var batchQuery = new List<string>();
            DateTime prevInsDueDate = startDate;
            DateTime prevPvnRcpDt = MatchWeekdayInSameWeek(dtFin, prevInsDueDate);
            for (int i = 1; i <= insDueDates.Count; i++)
            {
                DateTime newInsDueDate = prevInsDueDate.AddMonths(1);
                DateTime newPvnRcpDt;
                if (i == 0)
                {
                    newPvnRcpDt = MatchWeekdayInSameWeek(dtFin, newInsDueDate);
                }
                else
                {
                    newPvnRcpDt = prevPvnRcpDt.AddDays(28);
                }
                string update = $"UPDATE FICHQ SET INS_DUE_DT='{newInsDueDate.ToString("yyyy-MM-dd")}', PVN_RCP_DT='{newPvnRcpDt.ToString("yyyy-MM-dd")}' " +
                   $"WHERE CODE='{code}' AND INSTALL='{i}'";
                batchQuery.Add(update);
                prevInsDueDate = newInsDueDate;
                prevPvnRcpDt = newPvnRcpDt;
            }
            var query = string.Join("; ", batchQuery);
            // Update the database with new dates
            using (SqlCommand cmd = new SqlCommand())
            {
                cmd.Connection = con;
                cmd.Transaction = trans;
                cmd.CommandText = query;
                cmd.ExecuteNonQuery();
            }
        }

        //checking day of week
        private DateTime MatchWeekdayInSameWeek(DateTime referenceDate, DateTime targetDate)
        {
            DayOfWeek refDay = referenceDate.DayOfWeek;
            DateTime startOfWeek = targetDate.AddDays(-(int)targetDate.DayOfWeek);

            for (int i = 0; i < 7; i++)
            {
                DateTime potentialDate = startOfWeek.AddDays(i);
                if (potentialDate.DayOfWeek == refDay)
                {
                    return potentialDate;
                }
            }

            return targetDate;
        }

        public List<SMPostVM> GetRecordsFromSMPost(SqlConnection con)
        {
            List<SMPostVM> dataList = new List<SMPostVM>();
            string todate = DateTime.Now.ToString("yyyy-MM-dd");
            string fromdate = DateTime.Now.AddDays(-2).ToString("yyyy-MM-dd");

            //string todate = "2025 -07 -30";
            //string fromdate = "2025-03-29";

            try
            {              
                using (SqlCommand cmd = new SqlCommand("Usp_BulkSmPost", con))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@todate", todate);
                    cmd.Parameters.AddWithValue("@fromdate", fromdate);

                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            SMPostVM item = new SMPostVM
                            {
                                SMCode = reader["CODE"].ToString(),
                                ParytCode = reader["PARTY_CD"].ToString(),
                                CustName = reader["SUBS_NAME"].ToString(),
                                LoanAmt = Convert.ToString(reader["INVEST"]),
                                Duration = Convert.ToString(reader["DURATION"]),
                                Creator = reader["Creator"].ToString(),
                                PostSM = Convert.ToString(reader["PostSM"]),
                                FiCode = reader["FiCode"].ToString(),
                                PaymentDate = reader["PaymentDate"] != DBNull.Value ? Convert.ToDateTime(reader["PaymentDate"]) : DateTime.MinValue,
                                FiId = Convert.ToInt64(reader["FiId"])
                            };

                            dataList.Add(item);
                        }
                    }
                }

                if (dataList.Count == 0)
                {
                    Console.WriteLine($"No data found between {fromdate} and {todate}.");
                }
                else
                {
                    Console.WriteLine($"{dataList.Count} record(s) found between {fromdate} and {todate}.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("An error occurred while retrieving data: " + ex.Message);
            }

            return RemoveDuplicatesFromList(dataList, r => new { r.SMCode });
        }

        public static List<T> RemoveDuplicatesFromList<T, TKey>(List<T> list, Func<T, TKey> keySelector)
        {
            HashSet<TKey> set = new HashSet<TKey>();
            List<T> uniqueList = new List<T>();

            foreach (var item in list)
            {
                TKey key = keySelector(item);
                if (set.Add(key))
                {
                    uniqueList.Add(item);
                }
            }
            return uniqueList;
        }

        //Insertion values in sm and chq table     
        public void UpdateSmAndChq(string code, DateTime DTFin, string UserID, SqlConnection con, SqlTransaction trans)
        {
            try
            {
                SqlCommand cmd = new SqlCommand
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandText = "Usp_InsertIntoSmAndChq",
                    Connection = con,
                    Transaction = trans,
                    CommandTimeout = 0
                };

                cmd.Parameters.Add("@SmCode", SqlDbType.VarChar).Value = code;
                cmd.Parameters.Add("@DTFin", SqlDbType.DateTime).Value = DTFin;
                cmd.Parameters.Add("@UserID", SqlDbType.VarChar).Value = UserID;

                cmd.ExecuteNonQuery();
            }
            catch (Exception)
            {
                throw;
            }
        }

        //Updating status in SmCaseForPost status=0 for posted sm
        public void UpdateStatusForSmPost(string code, SqlConnection con, SqlTransaction trans)
        {
            SqlCommand cmd = new SqlCommand();
            cmd.CommandType = CommandType.Text;

            cmd.CommandText = "UPDATE PDLERP..SmCaseForPost SET IsSmPost=0 WHERE SmCode=@SmCode";
            cmd.Parameters.Add("@SmCode", SqlDbType.VarChar).Value = code;
            cmd.Connection = con;
            cmd.Transaction = trans;

            cmd.ExecuteNonQuery();
        }
        public bool IsSmCodeExistsInSmAndChq(string smCode)
        {
            using (SqlConnection con = new SqlConnection(_configuration.GetConnectionString("defaultconnections")))
            {
                con.Open();

                // Check in chq
                string chqQuery = @"IF EXISTS (SELECT 1 FROM chq WHERE CODE = @SmCode) SELECT 1 ELSE SELECT 0";
                using (SqlCommand chqCmd = new SqlCommand(chqQuery, con))
                {
                    chqCmd.Parameters.AddWithValue("@SmCode", smCode);
                    chqCmd.CommandTimeout = 120;
                    int chqExists = (int)chqCmd.ExecuteScalar();
                    if (chqExists == 0)
                        return false;
                }

                // Check in sm
                string smQuery = @"IF EXISTS (SELECT 1 FROM sm WHERE CODE = @SmCode) SELECT 1 ELSE SELECT 0";
                using (SqlCommand smCmd = new SqlCommand(smQuery, con))
                {
                    smCmd.Parameters.AddWithValue("@SmCode", smCode);
                    smCmd.CommandTimeout = 120;
                    int smExists = (int)smCmd.ExecuteScalar();
                    return smExists == 1;
                }
            }
        }

    }
}
