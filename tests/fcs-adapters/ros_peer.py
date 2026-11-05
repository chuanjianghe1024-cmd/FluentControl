"""CI peer in a separate Ethernet network namespace; no device controls."""
import rclpy
from std_msgs.msg import Float64, String, Bool

rclpy.init()
node = rclpy.create_node("fcs_ethernet_fixture")
light = node.create_publisher(Float64, "/test/light", 1)
acknowledgment = node.create_publisher(String, "/test/export_seen", 1)
node.create_subscription(String, "/fcs/telemetry/light", acknowledgment.publish, 1)
enabled = True


def stop(message):
    global enabled
    if message.data:
        enabled = False


def publish():
    if enabled:
        message = Float64(); message.data = 321.5
        light.publish(message)


node.create_subscription(Bool, "/test/stop_light", stop, 1)
node.create_timer(.1, publish)
try:
    rclpy.spin(node)
finally:
    node.destroy_node(); rclpy.shutdown()
