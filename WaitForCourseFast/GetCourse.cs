﻿using System;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;
using Locke_HTTPHelper;

namespace Locke_CourseSystem
{
    public class LoginException : Exception
    {
        public LoginException()
            : base("登录出错到达上限")
        { }
    }

    public class Semester
    {
        public string id;
        public string name;
    }

    public class CourseSystem
    {
        public delegate void OutputDelegate(string log);

        private HTTPHelper _httpHelper;
        private Readjpg _jpgreader;
        public string UserId;
        private string _userPassword;
        private OutputDelegate _logDelegate;
        private OutputDelegate _stateDelegate;

        public CourseSystem(string id, string password, OutputDelegate logDelegate, OutputDelegate stateDelegate)
        {
            _httpHelper = new HTTPHelper(10000);
            _jpgreader = new Readjpg();
            UserId = id;
            _userPassword = password;
            _logDelegate = logDelegate;
            _stateDelegate = stateDelegate;
        }

        public string HttpGet(string url)
        {
            while (true)
            {
                try
                {
                    return _httpHelper.HttpGet_String(url);
                }
                catch (WebException e)
                {
                    if (e.Message.Contains("403"))
                    {
                        OutputLog("被系统踢出，重新登录。");
                        Login();
                    }

                    throw e;
                }
            }
        }

        public string HttpPost(string url, string postStr)
        {
            return _httpHelper.HttpPost_String(url, postStr);
        }

        static string GetResponseJpgCode(HttpWebRequest request, Readjpg jpgreader)
        {
            var response = (HttpWebResponse)request.GetResponse();
            var rawStream = response.GetResponseStream();

            var jpgcode = jpgreader.Bmp2Text(rawStream);

            rawStream.Close();

            return jpgcode;
        }

        public void OutputLog(string str)
        {
            var output = string.Format("{0} {1}{2}",
                DateTime.Now.ToLongTimeString(), str, Environment.NewLine);
            _logDelegate(output);
        }

        public void OutputState(string str)
        {
            var output = string.Format("{0} {1}",
                DateTime.Now.ToLongTimeString(), str);
            _stateDelegate(output);
        }

        public static void Sleep(int milliseconds)
        {
            System.Threading.Thread.Sleep(milliseconds);
        }

        public void Login()
        {
            OutputLog("开始登录");

            {
                while (true)
                {
                    OutputLog("打开登录页面");
                    string loginUrl1 = "http://zhjwxk.cic.tsinghua.edu.cn/xsxk_index.jsp";
                    var html = HttpGet(loginUrl1);
                    if (!html.Contains("忙"))
                        break;

                    Sleep(300);
                }
            }

            {
                string loginUrl2 = "http://zhjwxk.cic.tsinghua.edu.cn/xklogin.do";
                HttpGet(loginUrl2);
            }

            int errorcount = 0;
            while (true)
            {
                string jpgcode = null;
                OutputLog("获取jpg");
                while (jpgcode == null)
                {
                    string loginUrl3 = "http://zhjwxk.cic.tsinghua.edu.cn/login-jcaptcah.jpg?captchaflag=login1";
                    HttpWebRequest request = _httpHelper.CreateHTTPGetRequest(loginUrl3, true);
                    jpgcode = GetResponseJpgCode(request, _jpgreader);

                    if (jpgcode == null)
                        OutputLog("识别jpg失败");

                    Sleep(1000);
                }

                OutputLog("开始HTTPS链接");
                {
                    string loginUrl4 = "https://zhjwxk.cic.tsinghua.edu.cn:443/j_acegi_formlogin_xsxk.do";
                    string postStr = string.Format("j_username={0}&j_password={1}&captchaflag=login1&_login_image_={2}",
                        UserId, _userPassword, jpgcode);
                    HttpWebRequest request = _httpHelper.CreateHTTPPOSTRequest(loginUrl4, postStr, true);
                    HTTPHelper.GetResponseBytes(request);
                }

                try
                {
                    bool loginIn = IsLoginIn();

                    if (loginIn)
                    {
                        OutputLog("登录成功");
                        break;
                    }
                    else
                    {
                        errorcount++;
                        OutputLog("登录失败，重新登录。");

                        Sleep(2000);
                    }
                }
                catch (Exception e)
                {
                    errorcount++;
                    OutputLog("登录过程中出现错误，重新登录。错误信息：" + e.Message);

                    Sleep(2000);
                }
            }
        }

        public bool IsLoginIn()
        {
            try
            {
                string url = "http://zhjwxk.cic.tsinghua.edu.cn/xkYjs.vxkYjsXkbBs.do?m=main";
                _httpHelper.HttpGet_String(url);
            }
            catch (WebException e)
            {
                if (e.Message.Contains("403"))
                    return false;

                throw e;
            }

            return true;
        }

        public Semester[] GetSemesterList()
        {
            string html;
            string regexPatternStr;

            {
                string url1 = "http://zhjwxk.cic.tsinghua.edu.cn/xkYjs.vxkYjsXkbBs.do?m=main";
                html = HttpGet(url1);
            }

            regexPatternStr = "function showTree(?:[^=]*=){3}([^\"]*)\"";
            string nowSemester = Regex.Match(html, regexPatternStr).Groups[1].ToString();

            {
                string url1 = "http://zhjwxk.cic.tsinghua.edu.cn/xkYjs.vxkYjsXkbBs.do?m=showTree&p_xnxq=" + nowSemester;
                html = HttpGet(url1);
            }

            regexPatternStr = "<option value=\"([^\"]*)\"[^>]*>\\s*([^\\s]*)\\s";
            MatchCollection matchResult = Regex.Matches(html, regexPatternStr);

            List<Semester> semesterList = new List<Semester>();
            foreach (Match match in matchResult)
            {
                semesterList.Add(
                    new Semester()
                    {
                        id = match.Groups[1].ToString(),
                        name = match.Groups[2].ToString()
                    });
            }

            return semesterList.ToArray();
        }

        public CourseHelper GetCourseHelper(string semesterID)
        {
            return new CourseHelper(this, semesterID);
        }

    }

    public class Course : IEquatable<Course>
    {
        public string 课程号;
        public string 课序号;
        public string 课程名;
        public string 主讲教师;
        public string 上课时间;
        public string 开课院系;
        public string 学分;

        public Course(string courseId, string courseSubId)
        {
            课程号 = courseId;
            课序号 = courseSubId;
        }

        #region IEquatable<Course> 成员

        public bool Equals(Course other)
        {
            return 课程号 == other.课程号 && 课序号 == other.课序号;
        }

        #endregion
    }

    public class CourseHelper
    {
        public CourseSystem CourseSystem;
        private string _semester;
        public List<Course> MyCourseList;

        public CourseHelper(CourseSystem courseSytemInfo, string semester)
        {
            CourseSystem = courseSytemInfo;
            _semester = semester;
        }

        string HttpGet(string url)
        {
            return CourseSystem.HttpGet(url);
        }

        string HttpPost(string url, string postStr)
        {
            return CourseSystem.HttpPost(url, postStr);
        }

        void OutputLog(string str)
        {
            CourseSystem.OutputLog(str);
        }

        void OutputState(string str)
        {
            CourseSystem.OutputState(str);
        }

        static Regex HTMLToTxtRegex = new Regex("(?:<[^>]*>)?([^<]*)(?:<[^>]*>)?");

        static string HTMLToTxt(string html)
        {
            return HTMLToTxtRegex.Match(html).Groups[1].ToString();
        }

        public Course[] GetMyCourseList()
        {
            string html;

            OutputLog("正在获取已选课程");

            {
                string url1 = "http://zhjwxk.cic.tsinghua.edu.cn/xkYjs.vxkYjsXkbBs.do?p_xnxq=" + _semester + "&pathContent=%D2%BB%BC%B6%BF%CE%B1%ED";
                html = HttpGet(url1);
            }

            string regexPatternStr = "\\[" +
                "\\s*\"([^\"]*)\"\\s*," +
                "\\s*\"([^\"]*)\"\\s*," +
                "\\s*\"([^\"]*)\"\\s*," +
                "\\s*\"([^\"]*)\"\\s*," +
                "\\s*\"([^\"]*)\"\\s*," +
                "\\s*\"([^\"]*)\"\\s*," +
                "\\s*\"([^\"]*)\"\\s*," +
                "\\s*\"([^\"]*)\"\\s*," +
                "\\s*\"([^\"]*)\"\\s*," +
                "\\s*\"([^\"]*)\"\\s*," +
                "\\s*\"([^\"]*)\"\\s*," +
                "\\s*\"([^\"]*)\"\\s*," +
                "\\s*\"([^\"]*)\"\\s*" +
                "\\]";

            Regex regex = new Regex(regexPatternStr);

            var courseList = new List<Course>();
            //    \[[^\[\]]*,[^\[\]]*\d+[^\[\]]*\]
            MatchCollection matchCC = Regex.Matches(html, "\\[[^\\[\\]]*,[^\\[\\]]*\\d+[^\\[\\]]*\\]");
            foreach (Match match0 in matchCC)
            {
                Match match = regex.Match(match0.ToString());
                if (!match.Success)
                {
                    OutputLog("获取已选课程列表：因为解析失败丢弃一个课程信息");
                    continue;
                }

                courseList.Add(new Course(match.Groups[2].ToString(), match.Groups[3].ToString())
                {
                    课程名 = HTMLToTxt(match.Groups[1].ToString()),
                    主讲教师 = match.Groups[5].ToString(),
                    上课时间 = match.Groups[6].ToString()
                });
            }

            MyCourseList = courseList;
            return MyCourseList.ToArray();
        }

        static string ParseToken(string html)
        {
            var matchresult = Regex.Match(html, "name=\"token\" value=\"([^\\\"]*)\"");
            return matchresult.Groups[1].ToString();
        }

        public bool FillCourseInfo(Course course)
        {
            string html;
            string token;

            {
                string url1 = "http://zhjwxk.cic.tsinghua.edu.cn/xkBks.vxkBksJxjhBs.do?m=kkxxSearch&p_xnxq=" + _semester;
                html = HttpGet(url1);
            }
            token = ParseToken(html);

            {
                string url2 = "http://zhjwxk.cic.tsinghua.edu.cn/xkBks.vxkBksJxjhBs.do";
                string postStr = string.Format("m=kkxxSearch&page=-1&token={0}&p_sort.p1=&p_sort.p2=&p_sort.asc1=true&p_sort.asc2=true&p_xnxq={1}&pathContent=&showtitle=&p_kkdwnm=&p_kch={2}&p_kcm=&p_zjjsxm=&p_kcflm=&p_skxq=&p_skjc=&p_xkwzsm=&p_kctsm=&p_kctsm_new_value=false&p_ssnj=&p_rxklxm=&goPageNumber=1",
                    token, _semester, course.课程号);

                html = HttpPost(url2, postStr);
            }

            string regexPatternStr = "\\[" +
                "\\s*\"([^\"]*)\"\\s*," +
                "\\s*\"([^\"]*)\"\\s*," +
                "\\s*\"([^\"]*)\"\\s*," +
                "\\s*\"([^\"]*)\"\\s*," +
                "\\s*\"([^\"]*)\"\\s*," +
                "\\s*\"([^\"]*)\"\\s*," +
                "\\s*\"([^\"]*)\"\\s*," +
                "\\s*\"([^\"]*)\"\\s*," +
                "\\s*\"([^\"]*)\"\\s*," +
                "\\s*\"([^\"]*)\"\\s*," +
                "\\s*\"([^\"]*)\"\\s*," +
                "\\s*\"([^\"]*)\"\\s*," +
                "\\s*\"([^\"]*)\"\\s*," +
                "\\s*\"([^\"]*)\"\\s*," +
                "\\s*\"([^\"]*)\"\\s*," +
                "\\s*\"([^\"]*)\"\\s*," +
                "\\s*\"([^\"]*)\"\\s*" +
                "\\]";

            var regex = new Regex(regexPatternStr);

            //    \[[^\[\]]*,[^\[\]]*\d+[^\[\]]*\]
            var matchCC = Regex.Matches(html, "\\[[^\\[\\]]*,[^\\[\\]]*\\d+[^\\[\\]]*\\]");
            foreach (Match match0 in matchCC)
            {
                Match match = regex.Match(match0.ToString());
                if (!match.Success)
                {
                    OutputLog("FillCourseInfo：因为解析失败丢弃一个课程信息");
                    continue;
                }

                if (course.课程号 == match.Groups[2].ToString() && course.课序号 == match.Groups[3].ToString())
                {
                    course.开课院系 = match.Groups[1].ToString();
                    course.课程名 = HTMLToTxt(match.Groups[4].ToString());
                    course.学分 = match.Groups[5].ToString();
                    course.主讲教师 = HTMLToTxt(match.Groups[6].ToString());
                    course.上课时间 = match.Groups[9].ToString();
                    return true;
                }
            }
            return false;
        }

        public bool TryGetCourse(Course course)
        {
            string html;
            string token;

            {
                string url1 = "http://zhjwxk.cic.tsinghua.edu.cn/xkYjs.vxkYjsXkbBs.do?m=xkqkSearch&p_xnxq=" + _semester;
                html = HttpGet(url1);
            }

            token = ParseToken(html);

            Sleep(1000);

            {
                string url2 = "http://zhjwxk.cic.tsinghua.edu.cn/xkBks.vxkBksJxjhBs.do";
                string postStr = string.Format("m=kylSearch&page=-1&token={0}&p_sort.p1=&p_sort.p2=&p_sort.asc1=&p_sort.asc2=&p_xnxq={1}&pathContent=%BF%CE%D3%E0%C1%BF%B2%E9%D1%AF&p_kch={2}&p_kxh={3}&p_kcm=&p_skxq=&p_skjc=&goPageNumber=1",
                    token, _semester, course.课程号, course.课序号);

                html = HttpPost(url2, postStr);
            }

            string matchPattern = string.Format("\"{0}\"\\s*,\"{1}\"\\s*,\"([^\"]*)\"\\s*,\"[^\"]*\"\\s*,\"([^\"]*)\"",
                course.课程号, course.课序号);
            Match matchresult = Regex.Match(html, matchPattern);
            string numStr = matchresult.Groups[2].ToString();
            string coursename = matchresult.Groups[1].ToString();
            course.课程名 = coursename;

            OutputState(string.Format(" 获取课余量成功，课程名称:{0}，课余量:{1}", coursename, numStr));
            int num = int.Parse(numStr);

            if (num == 0)
                return false;

            OutputLog("发现课余量，开始选课。课程名：" + course.课程名);


            //打开任选课界面
            {
                string url3 = string.Format("http://zhjwxk.cic.tsinghua.edu.cn/xkYjs.vxkYjsXkbBs.do?m=rxSearch&p_xnxq={0}&tokenPriFlag=rx&is_zyrxk=1",
                    _semester);

                html = HttpGet(url3);
            }

            token = ParseToken(html);

            //提交选课
            {
                string url4 = "http://zhjwxk.cic.tsinghua.edu.cn/xkYjs.vxkYjsXkbBs.do";
                string postStr = string.Format("m=saveRxKc&page=&token={0}&p_sort.p1=&p_sort.p2=&p_sort.asc1=true&p_sort.asc2=true&p_xnxq={1}&is_zyrxk=&tokenPriFlag=rx&p_kch={2}&p_kcm=&p_kkdwnm=&p_kctsm=&p_kctsm_new_value=false&p_rxklxm=&p_rx_id={1}%3B{2}%3B{3}%3B&goPageNumber=1",
                    token, _semester, course.课程号, course.课序号);

                html = HttpPost(url4, postStr);
            }

            if (html.Contains("提交选课成功"))
            {
                OutputLog("选课成功。课程名：" + course.课程名);
                return true;
            }
            else
            {
                OutputLog("选课失败。课程名：" + course.课程名);
                return false;
            }
        }

        private void Sleep(int p)
        {
            CourseSystem.Sleep(p);
        }

        public bool DelCourse(Course course)
        {
            string html;
            string token;

            {
                string url1 = string.Format("http://zhjwxk.cic.tsinghua.edu.cn/xkYjs.vxkYjsXkbBs.do?m=yxSearchTab&p_xnxq={0}&tokenPriFlag=yx",
                    _semester);

                html = HttpGet(url1);
            }

            token = ParseToken(html);

            Sleep(1000);

            {
                string url2 = "http://zhjwxk.cic.tsinghua.edu.cn/xkYjs.vxkYjsXkbBs.do";
                string postStr = string.Format("m=deleteYxk&token={0}&p_xnxq={1}&page=&tokenPriFlag=yx&tk=&jhzy_kch=&jhzy_kxh=&jhzy_zy=&p_del_id={1}%3B{2}%3B{3}%3B{4}%3B",
                    token, _semester, course.课程号, course.课序号, CourseSystem.UserId);

                html = HttpPost(url2, postStr);
            }

            if (html.Contains("删除选课成功"))
            {
                OutputLog("退课成功。课程名：" + course.课程名);
                return true;
            }
            else
            {
                OutputLog("退课失败。课程名：" + course.课程名);
                return false;
            }
        }

        public CourseScanner GetCourseScanner(IEnumerable<Course> courseWantedList)
        {
            return new CourseScanner(this, courseWantedList);
        }
    }

    public enum CoureseStateEnum
    {
        Wait,
        Got,
        Conflict
    }

    public class CourseStateData
    {
        public Course Course;
        public CoureseStateEnum State;

        public CourseStateData(Course course)
        {
            Course = course;
            State = CoureseStateEnum.Wait;
        }
    }

    public class CourseScanner
    {
        public CourseHelper CourseHelper;
        public List<CourseStateData> CourseStateList;
        List<CourseStateData> ScanCourseList;

        public CourseScanner(CourseHelper courseHelper, IEnumerable<Course> courseWantedList)
        {
            CourseHelper = courseHelper;
            CourseStateList = new List<CourseStateData>();
            ScanCourseList = new List<CourseStateData>();

            var courseIDList = new List<string>();
            foreach (Course c in courseHelper.MyCourseList)
            {
                if (!courseIDList.Contains(c.课程号))
                    courseIDList.Add(c.课程号);
            }

            foreach (Course c in courseWantedList)
            {
                var state = new CourseStateData(c);
                CourseStateList.Add(state);

                if (courseIDList.Contains(c.课程号))
                    state.State = CoureseStateEnum.Got;
                else
                    ScanCourseList.Add(state);
            }
        }

        public void ScanAll()
        {
            Random rand = new Random(5);
            for (int i = ScanCourseList.Count - 1; i >= 0; i--)
            {
                Sleep(2000 + rand.Next(1000));

                var courseState = ScanCourseList[i];
                if (courseState.State == CoureseStateEnum.Got)
                {
                    ScanCourseList.RemoveAt(i);
                    continue;
                }

                try
                {
                    if (CourseHelper.TryGetCourse(courseState.Course))
                    {
                        courseState.State = CoureseStateEnum.Got;
                        ScanCourseList.RemoveAt(i);
                        string id = courseState.Course.课程号;
                        foreach (CourseStateData c in ScanCourseList)
                        {
                            if (c.Course.课程号 == id)
                                c.State = CoureseStateEnum.Got;
                        }
                    }
                }
                catch (Exception e)
                {
                    if (e is LoginException)
                    {
                        throw e;
                    }

                    Sleep(1000 + rand.Next(1000));

                    if (!CourseHelper.CourseSystem.IsLoginIn())
                    {
                        Sleep(2000 + rand.Next(1000));

                        CourseHelper.CourseSystem.Login();

                        try
                        {
                            Sleep(2000 + rand.Next(1000));

                            if (CourseHelper.TryGetCourse(courseState.Course))
                            {
                                courseState.State = CoureseStateEnum.Got;
                                ScanCourseList.RemoveAt(i);
                            }
                        }
                        catch (Exception ee)
                        {
                            if (ee is LoginException)
                                throw ee;
                        }
                    }
                }
            }
        }

        private void Sleep(int p)
        {
            CourseSystem.Sleep(p);
        }
    }

}
