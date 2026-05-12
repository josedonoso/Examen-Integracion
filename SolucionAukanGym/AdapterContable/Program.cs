using Newtonsoft.Json.Linq;
using System;
using System.Messaging;

namespace AdapterContable
{
    internal class Program
    {
        private const string ColaPagos = @".\private$\jdo_pagos";
        private const string ColaEstadoCuenta = @".\private$\jdo_estado_cuenta";

        static void Main(string[] args)
        {
            Console.WriteLine("=== Adapter Contable - Aukan Gym ===");

            try
            {
                ValidarColas();
                int total = ProcesarPagos();

                Console.WriteLine();
                Console.WriteLine("Proceso finalizado correctamente");
                Console.WriteLine("Estados de cuenta enviados a MSMQ: " + total);

            }
            catch (Exception ex)
            {
                Console.WriteLine("ERROR");
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);
            }

            Console.WriteLine();
            Console.WriteLine("Presione una tecla para salir");
            Console.ReadKey();
        }

        private static void ValidarColas()
        {
            if (!MessageQueue.Exists(ColaPagos))
                throw new Exception("No existe la cola MSMQ: " + ColaPagos);
            if (!MessageQueue.Exists(ColaEstadoCuenta))
                throw new Exception("No existe la cola MSMQ: " + ColaEstadoCuenta);
        }

        private static int ProcesarPagos()
        {
            int contador = 0;

            using (MessageQueue origen = new MessageQueue(ColaPagos))
            using (MessageQueue destino = new MessageQueue(ColaEstadoCuenta))
            {
                origen.Formatter = new XmlMessageFormatter(new Type[] { typeof(string) });
                destino.Formatter = new XmlMessageFormatter(new Type[] { typeof(string) });

                while (true)
                {
                    Message mensaje;
                    try
                    {
                        mensaje = origen.Receive(TimeSpan.FromSeconds(2));
                    }
                    catch (MessageQueueException ex)
                    {
                        if (ex.MessageQueueErrorCode == MessageQueueErrorCode.IOTimeout)
                            break;

                        throw;
                    }

                    string pagoCanonicoJson = mensaje.Body.ToString();

                    Console.WriteLine();
                    Console.WriteLine("Pago Recibido desde jdo_pagos");
                    Console.WriteLine(pagoCanonicoJson);

                    string estadoCuentaJson = InvocarSistemaContable(pagoCanonicoJson);

                    Message mensajeEstado = new Message
                    {
                        Body = estadoCuentaJson,
                        Label = "Estado Cuenta"
                    };

                    destino.Send(mensajeEstado, MessageQueueTransactionType.Single);
                    contador++;
                    Console.WriteLine("Estado de Cuenta enviado a jdo_estado_cuenta" + contador);
                }
            }

            return contador;
        }

        private static string InvocarSistemaContable(string pagoCanonicoJson)
        {
            JObject pago = JObject.Parse(pagoCanonicoJson);

            string rutCliente = pago["rutCliente"] == null ? "" : pago["rutCliente"].ToString();
            decimal monto = pago["monto"] == null ? 0 : pago["monto"].Value<decimal>();


            var cliente = new ContabilidadWS.ContabilidadService();
            int montoEntero = Convert.ToInt32(monto);
            var respuesta = cliente.RegistrarPago(rutCliente, montoEntero, true);

            JObject estadoCuenta = new JObject
            {
                ["rutCliente"] = rutCliente,
                ["montoPago"] = montoEntero,
                ["saldo"] = respuesta.Saldo,
                ["estadoCuenta"] = respuesta.Saldo <= 0 ? "AL DIA" : "MOROSO",
                ["fechaProceso"] = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss")
            };
            return estadoCuenta.ToString(Newtonsoft.Json.Formatting.None);
        }
    }
}
