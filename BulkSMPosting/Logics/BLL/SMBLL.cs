using System.Data;
using System.Security.Claims;
using BulkSmPosting.Logics.BLL;
using BulkSMPosting.VM;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Protocols;

namespace BulkSMPosting.Logics.BLL
{
    public class SMBLL:BaseBLL
    {
        private readonly IConfiguration _configuration;
        SqlTransaction tr = null;
        public SMBLL(IConfiguration configuration)
        {
            _configuration = configuration;
        }
        public async Task SMPost()
         {
            using (SqlConnection con = new SqlConnection(_configuration.GetConnectionString("defaultconnection")))
            {
                await con.OpenAsync();
                try
                {
                    List<SMPostVM> obj = GetRecordsFromSMPost();
                    if (obj.Count > 0)
                    {
                        SqlCommand cmd = new SqlCommand();

                        cmd.CommandType = CommandType.Text;
                        cmd.Parameters.Add("@SmCode", SqlDbType.VarChar, 10);
                        cmd.Connection = con;

                        string UserID = "169";
                        List<string> smCodesForPdlShares = new List<string>();
                        DateTime TDate = DateTime.Now;
                        foreach (var item in obj)
                        {
                            int IsPosted = 0;
                            Int32 result = 0;
                            string code = item.SMCode;
                            string PartyCD = item.ParytCode;
                            string Location = item.Creator;
                            string BankCode = item.AheadsMain;
                            string ficode = item.FiCode;
                            int fiId = (int)item.FiId;
                            string Remarks = null;
                            DateTime paymentdate = (DateTime)item.PaymentDate;
                            cmd.Parameters["@SmCode"].Value = code;


                            using (SqlConnection conn = new SqlConnection(_configuration.GetConnectionString("defaultconnection")))
                            {
                                SqlCommand cmds = new SqlCommand();
                                cmds.CommandType = CommandType.Text;
                                Console.WriteLine($"FiCode: {ficode}, FiCreator: {Location}");
                                cmds.Parameters.Add("@fiId", SqlDbType.BigInt).Value = fiId;
                                cmds.Parameters.Add("@Creator", SqlDbType.VarChar).Value = Location.Trim();
                                cmds.CommandText = @"SELECT 1 FROM PDLSBI..SBILoanDisbStatus WHERE Fi_Id = @fiId";
                                cmds.Connection = conn;
                                conn.Open();

                                //1.s checking that account number exist or not fro SBI Case only
                                result = Convert.ToInt32(cmds.ExecuteScalar() ?? 0);
                            }

                                //2.s check in sm table already posted or not  
                                cmd.CommandText = @"select 1 from SM WHERE CODE=@SmCode";
                                IsPosted = Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
                            

                            //3.if this candition is true then this part of code is run
                            if (result > 0 || paymentdate != null && IsPosted == 0)
                            {
                                tr = con.BeginTransaction();
                                string DTFin = "";
                                if (item.PaymentDate != DateTime.MinValue)
                                {
                                    DateTime paymentDate = (DateTime)item.PaymentDate;
                                    UpdateInsDueDate(code, paymentDate, con, tr);
                                    DTFin = paymentDate.ToString("yyyy-MM-dd hh:mm:ss tt");
                                }

                                cmd.Transaction = tr;

                                UpdateSmAndChq(code, DTFin, UserID, con, tr);

                                smCodesForPdlShares.Add(code);

                                UpdateStatusForSmPost(code, con, tr);

                                tr.Commit();
                            }

                            else
                            {
                                if (result == 0)
                                {

                                    Remarks = "Account Number Not Found";

                                    if (result == 0 && paymentdate == null)
                                    {
                                        Remarks = "Account Number and PaymentDate Not Found";
                                    }
                                }

                                else if (paymentdate == null)
                                {

                                    Remarks = "PaymentDate is not found";
                                }

                                if (Remarks != null)
                                {
                                    //4.dumping values of unposted SM in NotPostedSM table
                                    //using (SqlCommand cmdn = new SqlCommand())
                                    //{
                                    //    cmdn.CommandType = CommandType.Text;

                                    //    cmdn.Parameters.AddWithValue("@SmCode", code);
                                    //    cmdn.Parameters.AddWithValue("@Remarks", Remarks);
                                    //    cmdn.CommandText = @"insert into NotPostedSM(SmCode,Remarks,Date)" +
                                    //                      "values(@SmCode,@Remarks,GETDATE())";
                                    //    cmdn.ExecuteNonQuery();
                                    //}
                                }
                            }
                        }
                        //5.new procedure fo smtransfer
                        //  zgenerateAllPdlShares(smCodesForPdlShares, con);

                        //6.Sending Unposted Sm on mail with remarks
                        // EmailSend();
                    }

                }
                catch (SqlException ex)
                {
                    tr.Rollback();
                    Helper.InsertLogException(ex, _configuration, "SMPOST_Schedular");

                }
                finally
                {
                    if (con != null && con.State == ConnectionState.Open)
                        con.Close();

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
            if (dtFin == DateTime.MinValue)
            {
                Console.WriteLine("Dt_Fin not found for the given code.");
                return;
            }

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

        public List<SMPostVM> GetRecordsFromSMPost()
        {
            List<SMPostVM> dataList = new List<SMPostVM>();
            string todate = DateTime.Now.ToString("yyyy-MM-dd");
            string fromdate = DateTime.Now.AddDays(-2).ToString("yyyy-MM-dd");

            using (SqlConnection con = new SqlConnection(_configuration.GetConnectionString("defaultconnection")))
            {
                using (SqlCommand cmd = new SqlCommand())
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.CommandText = "Usp_BulkSmPost";
                    cmd.Parameters.AddWithValue("@todate", todate);
                    cmd.Parameters.AddWithValue("@fromdate", fromdate);
                    cmd.Connection = con;
                    if (con.State != ConnectionState.Open)
                        con.Open();
                    SqlDataReader reader = cmd.ExecuteReader();

                    while (reader.Read())
                    {
                        SMPostVM item = new SMPostVM();
                        item.SMCode = reader["CODE"].ToString();
                        item.ParytCode = reader["PARTY_CD"].ToString();
                        item.CustName = reader["SUBS_NAME"].ToString();
                        item.LoanAmt = Convert.ToString(reader["INVEST"]);
                        item.Duration = Convert.ToString(reader["DURATION"]);
                        item.Creator = reader["Creator"].ToString();
                        item.PostSM = Convert.ToString(reader["PostSM"]);
                        item.FiCode = reader["FiCode"].ToString();
                        item.PaymentDate = reader["PaymentDate"] != DBNull.Value ? (DateTime)Convert.ToDateTime(reader["PaymentDate"]) : DateTime.MinValue;
                        item.FiId = (long)reader["FiId"];
                        dataList.Add(item);
                    }
                    reader.Close();
                    con.Close();
                    cmd.Dispose();
                }
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

        //Insertion values in sm and fichq table
        public void UpdateSmAndChq(string code, string DTFin, string UserID, SqlConnection con, SqlTransaction trans)
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
            cmd.Parameters.Add("@DTFin", SqlDbType.VarChar).Value = DTFin;
            cmd.Parameters.Add("@UserID", SqlDbType.VarChar).Value = UserID;

            cmd.ExecuteNonQuery();
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

        //final call of SM Post method
        //public void GenerateAllPdlShares(List<string> smCodesForPdlShares, SqlConnection con)
        //{
        //    string smCodeList = string.Join(",", smCodesForPdlShares);
        //    DateTime todate = DateTime.Now;
        //    DateTime fromdate = todate.AddDays(-2);


        //    SqlCommand cmd = new SqlCommand
        //    {
        //        CommandType = CommandType.StoredProcedure,
        //        CommandText = "zGenAllPdlShareJaspreet",
        //        Connection = con,
        //        CommandTimeout = 0
        //    };

        //    cmd.Parameters.AddWithValue("@todate", todate);
        //    cmd.Parameters.AddWithValue("@fromdate", fromdate);

        //    cmd.ExecuteNonQuery();
        //}
    }
}
