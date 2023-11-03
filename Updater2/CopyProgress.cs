namespace DS4Updater
{
    public class CopyProgress
    {
        public long BytesTransferred { get; set; }
        public long ExpectedBytes { get; set; }
        /// <summary>
        /// Percentage complete as a value from 0.0-1.0
        /// </summary>
        public double PercentComplete
        {
            get
            {
                return ExpectedBytes <= 0 ? 0 : BytesTransferred / (double)ExpectedBytes;
            }
        }

        public CopyProgress(long bytesTransferred, long bytesExpected)
        {
            BytesTransferred = bytesTransferred;
            ExpectedBytes = bytesExpected;
        }
    }
}