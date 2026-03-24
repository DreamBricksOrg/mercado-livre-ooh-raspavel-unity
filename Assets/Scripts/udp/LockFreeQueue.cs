internal class SingleLinkNode<T>
{
    public SingleLinkNode<T> Next;
    public T Item;
}

public class LockFreeQueue<T>
{

    SingleLinkNode<T> head;
    SingleLinkNode<T> tail;

    public LockFreeQueue()
    {
        head = new SingleLinkNode<T>();
        tail = head;
    }

    public void Enqueue(T item)
    {
        SingleLinkNode<T> node = new SingleLinkNode<T>();
        node.Item = item;
        tail.Next = node;
        tail = node;
    }

    public T Dequeue()
    {
        if (head == tail)
        {
            return default(T);
        }
        SingleLinkNode<T> oldHead = head;
        T result = oldHead.Next.Item;
        head = oldHead.Next;
        return result;
    }

    public bool Empty()
    {
        return (head == tail);
    }
}