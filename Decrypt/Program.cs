using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SalesTabletAppPayments.Helper;

namespace Decrypt
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.Write(AesCrypto.DecryptString("un8nMmv4pUZuQ9G5Uf6h67ddhjqMo1uW6kdnrS83xMEV7Rub_MQd9nMoio5wWcdu8QxTwPRESOHvkcNgZBUrHw!!"));
            Console.ReadLine();
        }
    }
}
