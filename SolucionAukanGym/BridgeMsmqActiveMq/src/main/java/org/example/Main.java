package org.example;

import org.apache.activemq.artemis.jms.client.ActiveMQConnectionFactory;


import javax.jms.Connection;
import javax.jms.ConnectionFactory;
import javax.jms.MessageProducer;
import javax.jms.Queue;
import javax.jms.Session;
import javax.jms.TextMessage;
import java.io.BufferedReader;
import java.io.InputStreamReader;
import java.nio.charset.StandardCharsets;
import java.util.Base64;

public class Main {

    private static final String MSMQ_QUEUE = ".\\private$\\jdo_estado_cuenta";

    private static final String ARTEMS_URL = "tcp://10.0.0.5:61616";
    private static final String ARTEMIS_USER = "admin";
    private static final String ARTEMIS_PASSWORD = "admin";
    private static final String ARTEMIS_QUEUE = "jdo_amq_estado_cuenta";

    public static void main(String[] args) {
        System.out.println("BRIDGE MSMQ a ACTIVE MQ - AUKAN GYM ");
        System.out.println("ORIGEN MSMQ: " + MSMQ_QUEUE);
        System.out.println("DESTINO ActiveMQ: " + ARTEMS_URL);
        System.out.println("COLA ActiveMQ: " + ARTEMIS_QUEUE);

        int enviados = 0;

        try {
            ConnectionFactory factory = new ActiveMQConnectionFactory(ARTEMS_URL);

            try (Connection connection = factory.createConnection(ARTEMIS_USER, ARTEMIS_PASSWORD)) {
                connection.start();

                Session session = connection.createSession(false, Session.AUTO_ACKNOWLEDGE);
                Queue queue = session.createQueue(ARTEMIS_QUEUE);
                MessageProducer producer = session.createProducer(queue);

                while (true) {
                    String mensajeMsmq = recibirMensajeDesdeMsmq();

                    if (mensajeMsmq == null || mensajeMsmq.isBlank()) {
                        break;
                    }

                    TextMessage mensajeArtemis = session.createTextMessage(mensajeMsmq);
                    producer.send(mensajeArtemis);
                    enviados++;

                    System.out.println("Mensaje enviado a ActiveMQ Artemis: " + enviados);
                    System.out.println(mensajeMsmq);
                    System.out.println();
                }

                producer.close();
                session.close();
            }

            System.out.println("Proceso finalizado correctamente");
            System.out.println("Total mensajes enviados a ActiveMQ  Artemis: " + enviados);
        } catch (Exception ex) {
            System.out.println("ERROR en Bridge: ");
            ex.printStackTrace();
        }

        System.out.println("Presione ENTER para salir...");
        try {
            System.in.read();

        } catch (Exception ignored) {
        }
    }

    private static String recibirMensajeDesdeMsmq() throws
            Exception {
        String script =
                "Add-Type -AssemblyName System.Messaging; " +
                        "$q = New-Object System.Messaging.MessageQueue('" + MSMQ_QUEUE + "'); " +
                        "$q.Formatter = New-Object System.Messaging.XmlMessageFormatter([string[]]@('System.String')); " +
                        "try { " +
                        "  if ($q.Transactional) { " +
                        "    $m = $q.Receive([TimeSpan]::FromSeconds(2), [System.Messaging.MessageQueueTransactionType]::Single); " +
                        "  } else { " +
                        "    $m = $q.Receive([TimeSpan]::FromSeconds(2)); " +
                        "  } " +
                        "  $body = [string]$m.Body; " +
                        "  [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($body)); " +
                        "} catch [System.Messaging.MessageQueueException] { " +
                        "  if ($_.Exception.MessageQueueErrorCode -eq [System.Messaging.MessageQueueErrorCode]::IOTimeout) { " +
                        "    '' " +
                        "  } else { throw } " +
                        "}";

        ProcessBuilder pb = new ProcessBuilder(
                "powershell.exe",
                "-NoProfile",
                "-ExecutionPolicy",
                "Bypass",
                "-Command",
                script
        );

        pb.redirectErrorStream(true);

        Process process = pb.start();

        StringBuilder output = new StringBuilder();

        try(BufferedReader reader = new BufferedReader(
                new InputStreamReader(process.getInputStream(), StandardCharsets.UTF_8))){

            String line;

            while ((line = reader.readLine()) != null){
                output.append(line.trim());
            }
        }

        int exitCode = process.waitFor();

        if (exitCode != 0){
            throw  new RuntimeException("Error leyendo MSMQ desde PowerShell. Codigo: "+exitCode+ ". Salida: "+output);
        }

        String base64 = output.toString().trim();

        if(base64.isEmpty()){
            return  null;
        }

        byte[] bytes = Base64.getDecoder().decode(base64);
        return new String(bytes, StandardCharsets.UTF_8);

    }
}