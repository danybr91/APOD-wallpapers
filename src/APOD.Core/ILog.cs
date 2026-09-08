namespace APOD.Core
{
    public interface ILog
    {
        void Info(string message);
        void Error(string message);
        void Line(string message);
    }
}
