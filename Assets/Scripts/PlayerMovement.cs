using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    public float speed = 5;
    public Rigidbody2D rb;
    public Animator anim;
    public int facingdirection = 1;

    void Start()
    {
    }

    void FixedUpdate()
    {
        float horizontal = Input.GetAxis("Horizontal");
        float vertical = Input.GetAxis("Vertical");

        anim.SetFloat("Horizontal", Mathf.Abs(horizontal));
        anim.SetFloat("Vertical", Mathf.Abs(vertical));

        if (horizontal > 0 && transform.localScale.x <0 || horizontal < 0 && transform.localScale.x > 0)
        {
            Flip();
        }


        rb.linearVelocity = new Vector2(horizontal, vertical) * speed;

    }

    void Flip()
    {
        facingdirection *= 1;
        transform.localScale = new Vector3(transform.localScale.x * -1, transform.localScale.y, transform.localScale.z);
    }
}